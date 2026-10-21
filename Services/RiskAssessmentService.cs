using DLDS.Data;
using DLDS.Models;
using Microsoft.EntityFrameworkCore;

namespace DLDS.Services
{
    public record RiskAssessmentResult(int RiskScore, RiskLevel RiskLevel, string Reason);

    public interface IRiskAssessmentService
    {
        /// <summary>
        /// Scores a candidate file event against active RiskRules plus built-in
        /// heuristics (rapid deletes, large files). Always returns an explainable reason.
        /// </summary>
        Task<RiskAssessmentResult> AssessAsync(
            string fileName,
            string filePath,
            EventType eventType,
            long? fileSizeBytes,
            int monitoredFolderId,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Calculates a 0-100 risk score for a file event.
    /// IMPORTANT: results describe "Potential Data Leakage Activity" / "Suspicious File Activity" —
    /// this system never claims to have proven an actual data leak occurred.
    /// </summary>
    public class RiskAssessmentService : IRiskAssessmentService
    {
        private static readonly HashSet<string> DefaultSensitiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv",
            ".sql", ".json", ".zip", ".rar", ".key", ".pem"
        };

        // Rapid-activity heuristic thresholds
        private const int RapidEventWindowSeconds = 60;
        private const int RapidDeleteThreshold = 3;
        private const long LargeFileBytes = 25 * 1024 * 1024; // 25 MB

        private readonly ApplicationDbContext _db;
        private readonly ILogger<RiskAssessmentService> _logger;

        public RiskAssessmentService(ApplicationDbContext db, ILogger<RiskAssessmentService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<RiskAssessmentResult> AssessAsync(
            string fileName,
            string filePath,
            EventType eventType,
            long? fileSizeBytes,
            int monitoredFolderId,
            CancellationToken ct = default)
        {
            var extension = Path.GetExtension(fileName);

            // 1. Check administrator-configured RiskRules first (most specific match wins).
            var activeRules = await _db.RiskRules
                .Where(r => r.IsActive)
                .ToListAsync(ct);

            var matchedRule = activeRules
                .Where(r => RuleMatches(r, eventType, extension, fileSizeBytes))
                .OrderByDescending(r => RuleSpecificity(r))
                .ThenByDescending(r => r.RiskScore)
                .FirstOrDefault();

            if (matchedRule != null)
            {
                var reason = $"Matched configured rule \"{matchedRule.RuleName}\": {matchedRule.Description}";
                return new RiskAssessmentResult(matchedRule.RiskScore, matchedRule.RiskLevel, reason);
            }

            // 2. Built-in heuristics when no explicit rule matches.
            var isSensitiveExt = DefaultSensitiveExtensions.Contains(extension);
            var isLargeFile = fileSizeBytes.HasValue && fileSizeBytes.Value >= LargeFileBytes;
            var isScriptOrExecutable = IsExecutableOrScript(extension);

            // Rapid deletion pattern in the same folder within the window.
            var windowStart = DateTime.UtcNow.AddSeconds(-RapidEventWindowSeconds);
            var recentDeletesInFolder = await _db.FileEvents
                .Where(e => e.MonitoredFolderId == monitoredFolderId
                            && e.EventType == EventType.Deleted
                            && e.EventTimestamp >= windowStart)
                .CountAsync(ct);

            if (eventType == EventType.Deleted && recentDeletesInFolder + 1 >= RapidDeleteThreshold)
            {
                if (isSensitiveExt)
                {
                    return new RiskAssessmentResult(95, RiskLevel.Critical,
                        $"Multiple sensitive files ({recentDeletesInFolder + 1}) deleted within {RapidEventWindowSeconds} seconds in the same monitored folder. Potential Data Leakage Activity.");
                }

                return new RiskAssessmentResult(80, RiskLevel.High,
                    $"Rapid, repeated deletions ({recentDeletesInFolder + 1} files within {RapidEventWindowSeconds} seconds) detected. Suspicious File Activity.");
            }

            // Duplicate file / Data staging heuristic
            if (eventType == EventType.Created || eventType == EventType.Modified)
            {
                bool isDuplicateName = IsDuplicateNamePattern(fileName);
                bool isDuplicateContent = await IsDuplicateInDatabaseAsync(fileName, fileSizeBytes, extension, monitoredFolderId, ct);

                if (isDuplicateName || isDuplicateContent)
                {
                    string dupReason = isDuplicateName 
                        ? $"Duplicate file copy pattern detected in filename (\"{fileName}\")"
                        : $"Duplicate file content or cross-folder replication detected for \"{fileName}\"";

                    if (isSensitiveExt)
                    {
                        return new RiskAssessmentResult(85, RiskLevel.Critical,
                            $"{dupReason} containing sensitive data ({extension}). High probability of data staging or unauthorized replication for data exfiltration.");
                    }

                    return new RiskAssessmentResult(65, RiskLevel.High,
                        $"{dupReason}. Suspicious unauthorized file duplication.");
                }
            }

            if (isScriptOrExecutable && (eventType == EventType.Created || eventType == EventType.Modified))
            {
                return new RiskAssessmentResult(70, RiskLevel.High,
                    $"{(eventType == EventType.Created ? "New" : "Modified")} executable/script file detected inside a monitored directory ({extension}). Suspicious File Activity.");
            }

            if (isSensitiveExt && eventType == EventType.Deleted)
            {
                return new RiskAssessmentResult(65, RiskLevel.High,
                    $"Potentially sensitive file ({extension}) was deleted. Suspicious File Activity.");
            }

            if (isSensitiveExt && isLargeFile)
            {
                return new RiskAssessmentResult(60, RiskLevel.High,
                    $"Large ({FormatBytes(fileSizeBytes)}) potentially sensitive file ({extension}) was {eventType.ToString().ToLower()}. Potential Data Leakage Activity.");
            }

            if (isLargeFile)
            {
                return new RiskAssessmentResult(40, RiskLevel.Medium,
                    $"Large file ({FormatBytes(fileSizeBytes)}) was {eventType.ToString().ToLower()}.");
            }

            if (isSensitiveExt)
            {
                return new RiskAssessmentResult(30, RiskLevel.Medium,
                    $"Potentially sensitive file type ({extension}) was {eventType.ToString().ToLower()}.");
            }

            // 3. Default: routine, low-risk activity.
            return new RiskAssessmentResult(10, RiskLevel.Low,
                $"Routine {eventType.ToString().ToLower()} event for a common file type.");
        }

        private static bool RuleMatches(RiskRule rule, EventType eventType, string extension, long? fileSizeBytes)
        {
            if (rule.EventType.HasValue && rule.EventType.Value != eventType) return false;

            if (!string.IsNullOrWhiteSpace(rule.FileExtension) &&
                !string.Equals(rule.FileExtension, extension, StringComparison.OrdinalIgnoreCase))
                return false;

            if (rule.MinimumFileSizeBytes.HasValue &&
                (!fileSizeBytes.HasValue || fileSizeBytes.Value < rule.MinimumFileSizeBytes.Value))
                return false;

            return true;
        }

        // More specific rules (extension + event type + size) should win over generic ones.
        private static int RuleSpecificity(RiskRule rule)
        {
            int score = 0;
            if (rule.EventType.HasValue) score++;
            if (!string.IsNullOrWhiteSpace(rule.FileExtension)) score++;
            if (rule.MinimumFileSizeBytes.HasValue) score++;
            return score;
        }

        private static bool IsExecutableOrScript(string extension)
        {
            var exts = new[] { ".exe", ".sh", ".ps1", ".bat", ".cmd", ".dll", ".app", ".command", ".scpt" };
            return exts.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        private static string FormatBytes(long? bytes)
        {
            if (!bytes.HasValue) return "unknown size";
            double mb = bytes.Value / (1024.0 * 1024.0);
            return $"{mb:0.0} MB";
        }

        private static bool IsDuplicateNamePattern(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(name)) return false;

            // Matches common macOS, Windows, and user duplication patterns:
            // "payroll copy", "payroll copy 2", "payroll - Copy", "payroll (1)", "copy of payroll", "payroll_copy", "payroll_backup"
            var pattern = @"(^copy(\s+of|\s*-)?\s+|[\s_-]copy(\s*\d+)?$|\(\d+\)$|[\s_-]dup(licate)?$|[\s_-]backup$)";
            return System.Text.RegularExpressions.Regex.IsMatch(name, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private async Task<bool> IsDuplicateInDatabaseAsync(string fileName, long? fileSizeBytes, string extension, int monitoredFolderId, CancellationToken ct)
        {
            try
            {
                if (fileSizeBytes.HasValue && fileSizeBytes.Value > 100)
                {
                    var sameContent = await _db.FileEvents
                        .AnyAsync(e => e.FileSize == fileSizeBytes.Value 
                                    && e.FileExtension == extension 
                                    && e.FileName != fileName 
                                    && e.EventType != EventType.Deleted, ct);

                    if (sameContent) return true;
                }

                var crossFolder = await _db.FileEvents
                    .AnyAsync(e => e.FileName.ToLower() == fileName.ToLower() 
                                && e.MonitoredFolderId != monitoredFolderId 
                                && e.EventType != EventType.Deleted, ct);

                return crossFolder;
            }
            catch
            {
                return false;
            }
        }
    }
}

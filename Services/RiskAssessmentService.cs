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
    }
}

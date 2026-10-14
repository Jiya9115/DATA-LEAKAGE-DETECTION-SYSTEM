using System.Text;
using DLDS.Data;
using DLDS.Models;
using Microsoft.EntityFrameworkCore;

namespace DLDS.Services
{
    public class ReportFilter
    {
        public DateTime FromUtc { get; set; } = DateTime.UtcNow.AddDays(-7);
        public DateTime ToUtc { get; set; } = DateTime.UtcNow;
        public EventType? EventType { get; set; }
        public RiskLevel? RiskLevel { get; set; }
        public int? MonitoredFolderId { get; set; }
        public string? FileExtension { get; set; }
    }

    public class ReportData
    {
        public int TotalEvents { get; set; }
        public Dictionary<string, int> EventsByType { get; set; } = new();
        public Dictionary<string, int> EventsByRiskLevel { get; set; } = new();
        public int CriticalEvents { get; set; }
        public int HighRiskEvents { get; set; }
        public List<FileEvent> TopEvents { get; set; } = new();
        public Dictionary<string, int> TopExtensions { get; set; } = new();
        public int MonitoredFoldersInScope { get; set; }
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
        public ReportFilter Filter { get; set; } = new();
    }

    public interface IReportService
    {
        Task<ReportData> GenerateAsync(ReportFilter filter, CancellationToken ct = default);
        Task<string> ExportCsvAsync(ReportFilter filter, CancellationToken ct = default);
    }

    /// <summary>
    /// Aggregates recorded FileEvents into a ReportData summary for the Report Center,
    /// and produces a CSV export of the matching raw events.
    /// </summary>
    public class ReportService : IReportService
    {
        private readonly ApplicationDbContext _db;

        public ReportService(ApplicationDbContext db)
        {
            _db = db;
        }

        private IQueryable<FileEvent> BuildQuery(ReportFilter filter)
        {
            var query = _db.FileEvents
                .Where(e => e.EventTimestamp >= filter.FromUtc && e.EventTimestamp <= filter.ToUtc);

            if (filter.EventType.HasValue)
                query = query.Where(e => e.EventType == filter.EventType.Value);

            if (filter.RiskLevel.HasValue)
                query = query.Where(e => e.RiskLevel == filter.RiskLevel.Value);

            if (filter.MonitoredFolderId.HasValue)
                query = query.Where(e => e.MonitoredFolderId == filter.MonitoredFolderId.Value);

            if (!string.IsNullOrWhiteSpace(filter.FileExtension))
                query = query.Where(e => e.FileExtension == filter.FileExtension);

            return query;
        }

        public async Task<ReportData> GenerateAsync(ReportFilter filter, CancellationToken ct = default)
        {
            var query = BuildQuery(filter);
            var events = await query.OrderByDescending(e => e.EventTimestamp).ToListAsync(ct);

            var data = new ReportData
            {
                Filter = filter,
                TotalEvents = events.Count,
                EventsByType = events.GroupBy(e => e.EventType.ToString())
                                      .ToDictionary(g => g.Key, g => g.Count()),
                EventsByRiskLevel = events.GroupBy(e => e.RiskLevel.ToString())
                                           .ToDictionary(g => g.Key, g => g.Count()),
                CriticalEvents = events.Count(e => e.RiskLevel == RiskLevel.Critical),
                HighRiskEvents = events.Count(e => e.RiskLevel == RiskLevel.High),
                TopEvents = events.Take(50).ToList(),
                TopExtensions = events.GroupBy(e => string.IsNullOrEmpty(e.FileExtension) ? "(none)" : e.FileExtension)
                                       .OrderByDescending(g => g.Count())
                                       .Take(10)
                                       .ToDictionary(g => g.Key, g => g.Count()),
                MonitoredFoldersInScope = events.Select(e => e.MonitoredFolderId).Distinct().Count()
            };

            return data;
        }

        public async Task<string> ExportCsvAsync(ReportFilter filter, CancellationToken ct = default)
        {
            var events = await BuildQuery(filter)
                .OrderByDescending(e => e.EventTimestamp)
                .ToListAsync(ct);

            var sb = new StringBuilder();
            sb.AppendLine("Id,FileName,FilePath,EventType,Extension,FileSizeBytes,RiskLevel,RiskScore,Timestamp,Description");

            foreach (var e in events)
            {
                sb.AppendLine(string.Join(",",
                    e.Id,
                    CsvEscape(e.FileName),
                    CsvEscape(e.FilePath),
                    e.EventType,
                    CsvEscape(e.FileExtension),
                    e.FileSize?.ToString() ?? "",
                    e.RiskLevel,
                    e.RiskScore,
                    e.EventTimestamp.ToString("o"),
                    CsvEscape(e.Description)));
            }

            return sb.ToString();
        }

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }
}

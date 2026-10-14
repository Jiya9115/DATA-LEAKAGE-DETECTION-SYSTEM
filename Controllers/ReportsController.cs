using System.Text;
using DLDS.Data;
using DLDS.Models;
using DLDS.Services;
using DLDS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DLDS.Controllers
{
    [Authorize]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IReportService _reports;

        public ReportsController(ApplicationDbContext db, IReportService reports)
        {
            _db = db;
            _reports = reports;
        }

        public async Task<IActionResult> Index(string preset = "last7", EventType? eventType = null,
            RiskLevel? riskLevel = null, int? monitoredFolderId = null, string? fileExtension = null,
            DateTime? from = null, DateTime? to = null)
        {
            var filter = BuildFilter(preset, eventType, riskLevel, monitoredFolderId, fileExtension, from, to);

            var vm = new ReportPageViewModel
            {
                Filter = filter,
                DateRangePreset = preset,
                Folders = await _db.MonitoredFolders.AsNoTracking().ToListAsync(),
                Report = await _reports.GenerateAsync(filter)
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> ExportCsv(string preset = "last7", EventType? eventType = null,
            RiskLevel? riskLevel = null, int? monitoredFolderId = null, string? fileExtension = null,
            DateTime? from = null, DateTime? to = null)
        {
            var filter = BuildFilter(preset, eventType, riskLevel, monitoredFolderId, fileExtension, from, to);
            var csv = await _reports.ExportCsvAsync(filter);

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "ReportGenerated",
                Description = $"Exported CSV report ({filter.FromUtc:yyyy-MM-dd} to {filter.ToUtc:yyyy-MM-dd}).",
                Username = User.Identity?.Name ?? "system",
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            });
            await _db.SaveChangesAsync();

            var bytes = Encoding.UTF8.GetBytes(csv);
            return File(bytes, "text/csv", $"DLDS_Report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
        }

        private static ReportFilter BuildFilter(string preset, EventType? eventType, RiskLevel? riskLevel,
            int? monitoredFolderId, string? fileExtension, DateTime? from, DateTime? to)
        {
            var now = DateTime.UtcNow;
            DateTime fromUtc, toUtc = now;

            switch (preset)
            {
                case "today":
                    fromUtc = now.Date;
                    break;
                case "last30":
                    fromUtc = now.AddDays(-30);
                    break;
                case "custom":
                    fromUtc = from ?? now.AddDays(-7);
                    toUtc = to ?? now;
                    break;
                default: // last7
                    fromUtc = now.AddDays(-7);
                    break;
            }

            return new ReportFilter
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                EventType = eventType,
                RiskLevel = riskLevel,
                MonitoredFolderId = monitoredFolderId,
                FileExtension = fileExtension
            };
        }
    }
}

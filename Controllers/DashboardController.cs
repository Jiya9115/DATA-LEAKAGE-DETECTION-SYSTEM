using DLDS.Data;
using DLDS.Models;
using DLDS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DLDS.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _db;

        public DashboardController(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var events = await _db.FileEvents.AsNoTracking().ToListAsync();
            var folders = await _db.MonitoredFolders.AsNoTracking().ToListAsync();
            var alerts = await _db.Alerts.AsNoTracking()
                .Include(a => a.FileEvent)
                .OrderByDescending(a => a.CreatedAt)
                .Take(8)
                .ToListAsync();

            var vm = new DashboardViewModel
            {
                TotalEvents = events.Count,
                FilesCreated = events.Count(e => e.EventType == EventType.Created),
                FilesModified = events.Count(e => e.EventType == EventType.Modified),
                FilesDeleted = events.Count(e => e.EventType == EventType.Deleted),
                FilesRenamed = events.Count(e => e.EventType == EventType.Renamed),
                HighRiskEvents = events.Count(e => e.RiskLevel == RiskLevel.High),
                CriticalAlerts = events.Count(e => e.RiskLevel == RiskLevel.Critical),
                ActiveMonitoredFolders = folders.Count(f => f.IsActive),
                TotalMonitoredFolders = folders.Count,
                EventsByType = events.GroupBy(e => e.EventType.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                EventsByRiskLevel = events.GroupBy(e => e.RiskLevel.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                TopExtensions = events
                    .GroupBy(e => string.IsNullOrEmpty(e.FileExtension) ? "(none)" : e.FileExtension)
                    .OrderByDescending(g => g.Count())
                    .Take(6)
                    .ToDictionary(g => g.Key, g => g.Count()),
                RecentEvents = events.OrderByDescending(e => e.EventTimestamp).Take(10).ToList(),
                RecentAlerts = alerts
            };

            // Last 7 days, bucketed by day, for the "Events over Time" chart.
            var today = DateTime.UtcNow.Date;
            for (int i = 6; i >= 0; i--)
            {
                var day = today.AddDays(-i);
                var count = events.Count(e => e.EventTimestamp.Date == day);
                vm.EventsOverTime.Add((day.ToString("MMM dd"), count));
            }

            return View(vm);
        }

        /// <summary>Lightweight JSON endpoint polled by the Live Monitor page.</summary>
        [HttpGet]
        public async Task<IActionResult> LatestEventsJson(int take = 15)
        {
            var events = await _db.FileEvents.AsNoTracking()
                .Include(e => e.MonitoredFolder)
                .OrderByDescending(e => e.EventTimestamp)
                .Take(take)
                .Select(e => new
                {
                    e.Id,
                    e.FileName,
                    e.FilePath,
                    EventType = e.EventType.ToString(),
                    RiskLevel = e.RiskLevel.ToString(),
                    e.RiskScore,
                    Timestamp = e.EventTimestamp,
                    Folder = e.MonitoredFolder != null ? e.MonitoredFolder.FolderName : ""
                })
                .ToListAsync();

            return Json(events);
        }
    }
}

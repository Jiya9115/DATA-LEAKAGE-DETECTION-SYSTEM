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
    public class MonitoringController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IFileMonitoringControl _monitoring;
        private readonly ILogger<MonitoringController> _logger;

        public MonitoringController(ApplicationDbContext db, IFileMonitoringControl monitoring, ILogger<MonitoringController> logger)
        {
            _db = db;
            _monitoring = monitoring;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var folders = await _db.MonitoredFolders.AsNoTracking().ToListAsync();
            var eventCounts = await _db.FileEvents.AsNoTracking()
                .GroupBy(e => e.MonitoredFolderId)
                .Select(g => new { FolderId = g.Key, Total = g.Count(), Risk = g.Count(e => e.RiskLevel == RiskLevel.High || e.RiskLevel == RiskLevel.Critical), Last = g.Max(e => e.EventTimestamp) })
                .ToListAsync();

            var vm = new MonitoringViewModel
            {
                Folders = folders.Select(f =>
                {
                    var stats = eventCounts.FirstOrDefault(x => x.FolderId == f.Id);
                    return new MonitoredFolderCardViewModel
                    {
                        Id = f.Id,
                        FolderName = f.FolderName,
                        FolderPath = f.FolderPath,
                        IsActive = f.IsActive,
                        IsCurrentlyWatching = _monitoring.IsWatching(f.Id),
                        TotalEvents = stats?.Total ?? 0,
                        RiskEvents = stats?.Risk ?? 0,
                        LastMonitoredAt = f.LastMonitoredAt,
                        LastEventAt = stats?.Last,
                        PathExists = Directory.Exists(f.FolderPath)
                    };
                }).OrderByDescending(f => f.IsActive).ThenBy(f => f.FolderName).ToList()
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(AddFolderViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Please provide a valid folder name and path.";
                return RedirectToAction(nameof(Index));
            }

            var path = model.FolderPath.Trim();

            if (!Directory.Exists(path))
            {
                TempData["Error"] = $"The folder \"{path}\" does not exist or the application does not have permission to access it.";
                return RedirectToAction(nameof(Index));
            }

            if (await _db.MonitoredFolders.AnyAsync(f => f.FolderPath == path))
            {
                TempData["Error"] = "This folder is already being monitored.";
                return RedirectToAction(nameof(Index));
            }

            var folder = new MonitoredFolder
            {
                FolderName = model.FolderName.Trim(),
                FolderPath = path,
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            };

            _db.MonitoredFolders.Add(folder);
            await LogAsync("FolderAdded", $"Added monitored folder \"{folder.FolderName}\" ({folder.FolderPath}).");
            await _db.SaveChangesAsync();

            if (model.StartImmediately)
            {
                await _monitoring.StartWatchingAsync(folder.Id);
                await LogAsync("MonitoringStarted", $"Started monitoring \"{folder.FolderName}\".");
                await _db.SaveChangesAsync();
            }

            TempData["Success"] = $"Folder \"{folder.FolderName}\" added successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Start(int id)
        {
            var folder = await _db.MonitoredFolders.FindAsync(id);
            if (folder == null) return NotFound();

            if (!Directory.Exists(folder.FolderPath))
            {
                TempData["Error"] = $"Cannot start monitoring — the folder \"{folder.FolderPath}\" no longer exists.";
                return RedirectToAction(nameof(Index));
            }

            await _monitoring.StartWatchingAsync(id);
            await LogAsync("MonitoringStarted", $"Started monitoring \"{folder.FolderName}\".");
            await _db.SaveChangesAsync();

            TempData["Success"] = "Monitoring started.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Stop(int id)
        {
            var folder = await _db.MonitoredFolders.FindAsync(id);
            if (folder == null) return NotFound();

            await _monitoring.StopWatchingAsync(id);
            await LogAsync("MonitoringStopped", $"Stopped monitoring \"{folder.FolderName}\".");
            await _db.SaveChangesAsync();

            TempData["Success"] = "Monitoring paused.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = Roles.Administrator)]
        public async Task<IActionResult> Delete(int id)
        {
            var folder = await _db.MonitoredFolders.FindAsync(id);
            if (folder == null) return NotFound();

            await _monitoring.StopWatchingAsync(id);

            _db.MonitoredFolders.Remove(folder);
            await LogAsync("FolderRemoved", $"Removed monitored folder \"{folder.FolderName}\".");
            await _db.SaveChangesAsync();

            TempData["Success"] = "Folder removed.";
            return RedirectToAction(nameof(Index));
        }

        private async Task LogAsync(string action, string description)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                Action = action,
                Description = description,
                Username = User.Identity?.Name ?? "system",
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            });
        }
    }
}

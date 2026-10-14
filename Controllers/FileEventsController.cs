using DLDS.Data;
using DLDS.Models;
using DLDS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DLDS.Controllers
{
    [Authorize]
    public class FileEventsController : Controller
    {
        private readonly ApplicationDbContext _db;

        public FileEventsController(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index(FileEventListViewModel filter)
        {
            var query = _db.FileEvents.AsNoTracking().Include(e => e.MonitoredFolder).AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var term = filter.Search.Trim();
                query = query.Where(e => e.FileName.Contains(term) || e.FilePath.Contains(term));
            }

            if (filter.EventType.HasValue)
                query = query.Where(e => e.EventType == filter.EventType.Value);

            if (filter.RiskLevel.HasValue)
                query = query.Where(e => e.RiskLevel == filter.RiskLevel.Value);

            if (filter.MonitoredFolderId.HasValue)
                query = query.Where(e => e.MonitoredFolderId == filter.MonitoredFolderId.Value);

            if (filter.FromDate.HasValue)
                query = query.Where(e => e.EventTimestamp >= filter.FromDate.Value);

            if (filter.ToDate.HasValue)
                query = query.Where(e => e.EventTimestamp <= filter.ToDate.Value.AddDays(1).AddTicks(-1));

            query = (filter.SortBy, filter.SortDescending) switch
            {
                ("FileName", true) => query.OrderByDescending(e => e.FileName),
                ("FileName", false) => query.OrderBy(e => e.FileName),
                ("RiskScore", true) => query.OrderByDescending(e => e.RiskScore),
                ("RiskScore", false) => query.OrderBy(e => e.RiskScore),
                (_, false) => query.OrderBy(e => e.EventTimestamp),
                _ => query.OrderByDescending(e => e.EventTimestamp)
            };

            filter.TotalCount = await query.CountAsync();

            var page = Math.Max(1, filter.Page);
            var pageSize = filter.PageSize is > 0 and <= 200 ? filter.PageSize : 25;

            filter.Events = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            filter.Folders = await _db.MonitoredFolders.AsNoTracking().ToListAsync();
            filter.Page = page;
            filter.PageSize = pageSize;

            return View(filter);
        }

        public async Task<IActionResult> Details(int id)
        {
            var fileEvent = await _db.FileEvents.AsNoTracking()
                .Include(e => e.MonitoredFolder)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (fileEvent == null) return NotFound();

            return View(fileEvent);
        }
    }
}

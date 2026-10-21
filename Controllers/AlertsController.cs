using DLDS.Data;
using DLDS.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DLDS.Controllers
{
    [Authorize]
    public class AlertsController : Controller
    {
        private readonly ApplicationDbContext _db;

        public AlertsController(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index(RiskLevel? riskLevel, DateTime? fromDate, bool unreadOnly = false)
        {
            bool isAdmin = User.IsInRole(Roles.Administrator);
            var currentUsername = User.Identity?.Name ?? "";

            var query = _db.Alerts.AsNoTracking().Include(a => a.FileEvent).AsQueryable();

            if (!isAdmin)
            {
                query = query.Where(a => a.FileEvent != null && (a.FileEvent.UploadedBy == currentUsername || (a.FileEvent.UploadedBy == null && a.FileEvent.IsSampleData)));
            }

            if (riskLevel.HasValue)
                query = query.Where(a => a.RiskLevel == riskLevel.Value);

            if (fromDate.HasValue)
                query = query.Where(a => a.CreatedAt >= fromDate.Value);

            if (unreadOnly)
                query = query.Where(a => !a.IsRead);

            var alerts = await query.OrderByDescending(a => a.CreatedAt).ToListAsync();

            ViewBag.RiskLevel = riskLevel;
            ViewBag.FromDate = fromDate;
            ViewBag.UnreadOnly = unreadOnly;
            ViewBag.IsAdmin = isAdmin;
            ViewBag.UnreadCount = await query.CountAsync(a => !a.IsRead);

            return View(alerts);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkRead(int id)
        {
            var alert = await _db.Alerts.Include(a => a.FileEvent).FirstOrDefaultAsync(a => a.Id == id);
            if (alert == null) return NotFound();

            bool isAdmin = User.IsInRole(Roles.Administrator);
            var currentUsername = User.Identity?.Name ?? "";

            if (!isAdmin && alert.FileEvent != null && !alert.FileEvent.IsSampleData && !string.IsNullOrEmpty(alert.FileEvent.UploadedBy) && !string.Equals(alert.FileEvent.UploadedBy, currentUsername, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            alert.IsRead = true;
            await _db.SaveChangesAsync();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Ok();

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllRead()
        {
            bool isAdmin = User.IsInRole(Roles.Administrator);
            var currentUsername = User.Identity?.Name ?? "";

            var query = _db.Alerts.Include(a => a.FileEvent).Where(a => !a.IsRead);
            if (!isAdmin)
            {
                query = query.Where(a => a.FileEvent != null && (a.FileEvent.UploadedBy == currentUsername || (a.FileEvent.UploadedBy == null && a.FileEvent.IsSampleData)));
            }

            var unread = await query.ToListAsync();
            foreach (var a in unread) a.IsRead = true;
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}

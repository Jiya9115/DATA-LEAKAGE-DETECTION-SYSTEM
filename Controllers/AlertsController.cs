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
            var query = _db.Alerts.AsNoTracking().Include(a => a.FileEvent).AsQueryable();

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
            ViewBag.UnreadCount = await _db.Alerts.CountAsync(a => !a.IsRead);

            return View(alerts);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkRead(int id)
        {
            var alert = await _db.Alerts.FindAsync(id);
            if (alert == null) return NotFound();

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
            var unread = await _db.Alerts.Where(a => !a.IsRead).ToListAsync();
            foreach (var a in unread) a.IsRead = true;
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Marked {unread.Count} alert(s) as read.";
            return RedirectToAction(nameof(Index));
        }
    }
}

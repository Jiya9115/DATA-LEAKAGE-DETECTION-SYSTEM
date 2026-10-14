using DLDS.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DLDS.Controllers
{
    [Authorize]
    public class AuditLogsController : Controller
    {
        private readonly ApplicationDbContext _db;

        public AuditLogsController(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index(int take = 100)
        {
            var logs = await _db.AuditLogs.AsNoTracking()
                .OrderByDescending(l => l.Timestamp)
                .Take(Math.Clamp(take, 10, 500))
                .ToListAsync();

            return View(logs);
        }
    }
}

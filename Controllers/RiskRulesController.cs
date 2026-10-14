using DLDS.Data;
using DLDS.Models;
using DLDS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DLDS.Controllers
{
    [Authorize(Roles = Roles.Administrator)]
    public class RiskRulesController : Controller
    {
        private readonly ApplicationDbContext _db;

        public RiskRulesController(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var rules = await _db.RiskRules.AsNoTracking().OrderByDescending(r => r.IsActive).ThenByDescending(r => r.RiskScore).ToListAsync();
            return View(rules);
        }

        [HttpGet]
        public IActionResult Create() => View("Form", new RiskRuleFormViewModel());

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var rule = await _db.RiskRules.FindAsync(id);
            if (rule == null) return NotFound();

            var vm = new RiskRuleFormViewModel
            {
                Id = rule.Id,
                RuleName = rule.RuleName,
                Description = rule.Description,
                EventType = rule.EventType,
                FileExtension = rule.FileExtension,
                MinimumFileSizeMb = rule.MinimumFileSizeBytes.HasValue ? rule.MinimumFileSizeBytes.Value / (1024 * 1024) : null,
                RiskScore = rule.RiskScore,
                RiskLevel = rule.RiskLevel,
                IsActive = rule.IsActive
            };

            return View("Form", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(RiskRuleFormViewModel model)
        {
            if (!ModelState.IsValid) return View("Form", model);

            RiskRule rule;
            var isNew = model.Id == 0;

            if (isNew)
            {
                rule = new RiskRule();
                _db.RiskRules.Add(rule);
            }
            else
            {
                var existing = await _db.RiskRules.FindAsync(model.Id);
                if (existing == null) return NotFound();
                rule = existing;
            }

            rule.RuleName = model.RuleName.Trim();
            rule.Description = model.Description.Trim();
            rule.EventType = model.EventType;
            rule.FileExtension = string.IsNullOrWhiteSpace(model.FileExtension) ? null : NormalizeExtension(model.FileExtension);
            rule.MinimumFileSizeBytes = model.MinimumFileSizeMb.HasValue ? model.MinimumFileSizeMb.Value * 1024 * 1024 : null;
            rule.RiskScore = model.RiskScore;
            rule.RiskLevel = model.RiskLevel;
            rule.IsActive = model.IsActive;

            _db.AuditLogs.Add(new AuditLog
            {
                Action = isNew ? "RiskRuleCreated" : "RiskRuleModified",
                Description = $"{(isNew ? "Created" : "Updated")} risk rule \"{rule.RuleName}\".",
                Username = User.Identity?.Name ?? "system",
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            });

            await _db.SaveChangesAsync();

            TempData["Success"] = $"Risk rule \"{rule.RuleName}\" saved.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var rule = await _db.RiskRules.FindAsync(id);
            if (rule == null) return NotFound();

            rule.IsActive = !rule.IsActive;
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var rule = await _db.RiskRules.FindAsync(id);
            if (rule == null) return NotFound();

            _db.RiskRules.Remove(rule);
            _db.AuditLogs.Add(new AuditLog
            {
                Action = "RiskRuleDeleted",
                Description = $"Deleted risk rule \"{rule.RuleName}\".",
                Username = User.Identity?.Name ?? "system",
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            });
            await _db.SaveChangesAsync();

            TempData["Success"] = "Risk rule deleted.";
            return RedirectToAction(nameof(Index));
        }

        private static string NormalizeExtension(string ext)
        {
            ext = ext.Trim();
            return ext.StartsWith('.') ? ext : "." + ext;
        }
    }
}

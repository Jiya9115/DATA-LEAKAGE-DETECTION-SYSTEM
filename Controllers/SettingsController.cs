using DLDS.Data;
using DLDS.Models;
using DLDS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DLDS.Controllers
{
    [Authorize(Roles = Roles.Administrator)]
    public class SettingsController : Controller
    {
        private readonly ApplicationDbContext _db;

        public SettingsController(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var settings = await _db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);

            var vm = new SettingsViewModel
            {
                SensitiveExtensions = settings.GetValueOrDefault(SettingKeys.SensitiveExtensions, ".pdf,.docx,.xlsx"),
                LargeFileThresholdMb = int.TryParse(settings.GetValueOrDefault(SettingKeys.LargeFileThresholdMb, "25"), out var mb) ? mb : 25,
                AlertOnHighRisk = settings.GetValueOrDefault(SettingKeys.AlertOnHighRisk, "true") == "true",
                AlertOnCriticalRisk = settings.GetValueOrDefault(SettingKeys.AlertOnCriticalRisk, "true") == "true",
                DefaultTheme = settings.GetValueOrDefault(SettingKeys.DefaultTheme, "dark")
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(SettingsViewModel model)
        {
            await UpsertAsync(SettingKeys.SensitiveExtensions, model.SensitiveExtensions);
            await UpsertAsync(SettingKeys.LargeFileThresholdMb, model.LargeFileThresholdMb.ToString());
            await UpsertAsync(SettingKeys.AlertOnHighRisk, model.AlertOnHighRisk ? "true" : "false");
            await UpsertAsync(SettingKeys.AlertOnCriticalRisk, model.AlertOnCriticalRisk ? "true" : "false");
            await UpsertAsync(SettingKeys.DefaultTheme, model.DefaultTheme);

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "SettingsUpdated",
                Description = "Application settings were updated.",
                Username = User.Identity?.Name ?? "system",
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            });

            await _db.SaveChangesAsync();

            TempData["Success"] = "Settings saved.";
            return RedirectToAction(nameof(Index));
        }

        private async Task UpsertAsync(string key, string value)
        {
            var existing = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (existing == null)
            {
                _db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
            }
            else
            {
                existing.Value = value;
                existing.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}

using DLDS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DLDS.Data
{
    /// <summary>
    /// Seeds roles, a demo administrator/user account, and realistic sample data so
    /// the app is demonstrable immediately after `dotnet ef database update`.
    /// Every seeded row has IsSampleData = true so it can be identified and purged later.
    /// </summary>
    public static class DbInitializer
    {
        public static async Task SeedAsync(IServiceProvider services)
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

            await SeedRolesAsync(roleManager);
            await SeedUsersAsync(userManager);

            if (!await db.RiskRules.AnyAsync())
                SeedRiskRules(db);

            MonitoredFolder? demoFolder = null;
            if (!await db.MonitoredFolders.AnyAsync())
                demoFolder = await SeedFolderAsync(db);
            else
                demoFolder = await db.MonitoredFolders.FirstAsync();

            if (!await db.FileEvents.AnyAsync())
                await SeedFileEventsAndAlertsAsync(db, demoFolder!);

            if (!await db.AuditLogs.AnyAsync())
                SeedAuditLogs(db);

            if (!await db.AppSettings.AnyAsync())
                SeedAppSettings(db);

            await db.SaveChangesAsync();
        }

        private static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
        {
            foreach (var role in new[] { Roles.Administrator, Roles.User })
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        private static async Task SeedUsersAsync(UserManager<ApplicationUser> userManager)
        {
            // NOTE: these are demo credentials for local/viva use only — change or remove before any real deployment.
            await EnsureUserAsync(userManager, "admin@dlds.local", "Admin@12345", Roles.Administrator, "System Administrator");
            await EnsureUserAsync(userManager, "analyst@dlds.local", "Analyst@12345", Roles.User, "Security Analyst");
        }

        private static async Task EnsureUserAsync(UserManager<ApplicationUser> userManager, string email, string password, string role, string displayName)
        {
            var existing = await userManager.FindByEmailAsync(email);
            if (existing != null) return;

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = displayName,
                CreatedAt = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(user, password);
            if (result.Succeeded)
                await userManager.AddToRoleAsync(user, role);
        }

        private static void SeedRiskRules(ApplicationDbContext db)
        {
            db.RiskRules.AddRange(
                new RiskRule
                {
                    RuleName = "Sensitive Spreadsheet Modification",
                    Description = "A spreadsheet was modified inside a monitored directory.",
                    EventType = EventType.Modified,
                    FileExtension = ".xlsx",
                    MinimumFileSizeBytes = 5L * 1024 * 1024,
                    RiskScore = 75,
                    RiskLevel = RiskLevel.High,
                    IsActive = true,
                    IsSampleData = true
                },
                new RiskRule
                {
                    RuleName = "Database Dump Created",
                    Description = "A .sql database dump file was created — review for unauthorized export.",
                    EventType = EventType.Created,
                    FileExtension = ".sql",
                    RiskScore = 70,
                    RiskLevel = RiskLevel.High,
                    IsActive = true,
                    IsSampleData = true
                },
                new RiskRule
                {
                    RuleName = "Archive Deleted",
                    Description = "A compressed archive was deleted from a monitored folder.",
                    EventType = EventType.Deleted,
                    FileExtension = ".zip",
                    RiskScore = 55,
                    RiskLevel = RiskLevel.Medium,
                    IsActive = true,
                    IsSampleData = true
                },
                new RiskRule
                {
                    RuleName = "Script File Created",
                    Description = "A shell/PowerShell script was created inside a monitored directory.",
                    EventType = EventType.Created,
                    FileExtension = ".ps1",
                    RiskScore = 80,
                    RiskLevel = RiskLevel.Critical,
                    IsActive = true,
                    IsSampleData = true
                }
            );
        }

        private static async Task<MonitoredFolder> SeedFolderAsync(ApplicationDbContext db)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var folder = new MonitoredFolder
            {
                FolderName = "Documents Security",
                FolderPath = Path.Combine(home, "Documents", "SensitiveData"),
                IsActive = false,
                CreatedAt = DateTime.UtcNow.AddDays(-14),
                LastMonitoredAt = DateTime.UtcNow.AddHours(-2),
                IsSampleData = true
            };
            db.MonitoredFolders.Add(folder);
            await db.SaveChangesAsync();
            return folder;
        }

        private static async Task SeedFileEventsAndAlertsAsync(ApplicationDbContext db, MonitoredFolder folder)
        {
            var now = DateTime.UtcNow;

            var events = new List<FileEvent>
            {
                new() { FileName = "report.docx", FilePath = $"{folder.FolderPath}/report.docx", EventType = EventType.Modified,
                        EventTimestamp = now.AddHours(-1), FileExtension = ".docx", FileSize = 2_400_000,
                        RiskLevel = RiskLevel.Low, RiskScore = 15, Description = "Routine modified event for a common file type.",
                        MonitoredFolderId = folder.Id, IsSampleData = true },

                new() { FileName = "employee_data.xlsx", FilePath = $"{folder.FolderPath}/employee_data.xlsx", EventType = EventType.Created,
                        EventTimestamp = now.AddHours(-2), FileExtension = ".xlsx", FileSize = 6_800_000,
                        RiskLevel = RiskLevel.High, RiskScore = 75, Description = "Matched configured rule \"Sensitive Spreadsheet Modification\": A spreadsheet was modified inside a monitored directory.",
                        MonitoredFolderId = folder.Id, IsSampleData = true },

                new() { FileName = "backup.zip", FilePath = $"{folder.FolderPath}/backup.zip", EventType = EventType.Deleted,
                        EventTimestamp = now.AddHours(-3), FileExtension = ".zip", FileSize = null,
                        RiskLevel = RiskLevel.Medium, RiskScore = 55, Description = "Matched configured rule \"Archive Deleted\": A compressed archive was deleted from a monitored folder.",
                        MonitoredFolderId = folder.Id, IsSampleData = true },

                new() { FileName = "script.ps1", FilePath = $"{folder.FolderPath}/script.ps1", EventType = EventType.Created,
                        EventTimestamp = now.AddHours(-4), FileExtension = ".ps1", FileSize = 4_200,
                        RiskLevel = RiskLevel.Critical, RiskScore = 80, Description = "Matched configured rule \"Script File Created\": A shell/PowerShell script was created inside a monitored directory.",
                        MonitoredFolderId = folder.Id, IsSampleData = true },

                new() { FileName = "database.sql", FilePath = $"{folder.FolderPath}/database.sql", EventType = EventType.Modified,
                        EventTimestamp = now.AddHours(-5), FileExtension = ".sql", FileSize = 15_000_000,
                        RiskLevel = RiskLevel.High, RiskScore = 70, Description = "Matched configured rule \"Database Dump Created\": A .sql database dump file was created — review for unauthorized export.",
                        MonitoredFolderId = folder.Id, IsSampleData = true },
            };

            db.FileEvents.AddRange(events);
            await db.SaveChangesAsync(); // need Ids before creating alerts

            foreach (var e in events.Where(e => e.RiskLevel is RiskLevel.High or RiskLevel.Critical))
            {
                db.Alerts.Add(new Alert
                {
                    FileEventId = e.Id,
                    Title = $"{(e.RiskLevel == RiskLevel.Critical ? "Critical" : "High risk")}: {e.EventType} — {e.FileName}",
                    Message = e.Description,
                    RiskLevel = e.RiskLevel,
                    IsRead = false,
                    CreatedAt = e.EventTimestamp,
                    IsSampleData = true
                });
            }
        }

        private static void SeedAuditLogs(ApplicationDbContext db)
        {
            var now = DateTime.UtcNow;
            db.AuditLogs.AddRange(
                new AuditLog { Action = "Login", Description = "Administrator signed in.", Timestamp = now.AddHours(-6), Username = "admin@dlds.local", IPAddress = "127.0.0.1", IsSampleData = true },
                new AuditLog { Action = "FolderAdded", Description = "Added monitored folder \"Documents Security\".", Timestamp = now.AddDays(-14), Username = "admin@dlds.local", IPAddress = "127.0.0.1", IsSampleData = true },
                new AuditLog { Action = "MonitoringStarted", Description = "Started monitoring \"Documents Security\".", Timestamp = now.AddHours(-5), Username = "admin@dlds.local", IPAddress = "127.0.0.1", IsSampleData = true },
                new AuditLog { Action = "RiskRuleModified", Description = "Updated risk rule \"Sensitive Spreadsheet Modification\".", Timestamp = now.AddHours(-3), Username = "admin@dlds.local", IPAddress = "127.0.0.1", IsSampleData = true },
                new AuditLog { Action = "ReportGenerated", Description = "Generated a 7-day activity report.", Timestamp = now.AddHours(-1), Username = "analyst@dlds.local", IPAddress = "127.0.0.1", IsSampleData = true }
            );
        }

        private static void SeedAppSettings(ApplicationDbContext db)
        {
            db.AppSettings.AddRange(
                new AppSetting { Key = SettingKeys.SensitiveExtensions, Value = ".pdf,.doc,.docx,.xls,.xlsx,.csv,.sql,.json,.zip,.rar,.key,.pem" },
                new AppSetting { Key = SettingKeys.LargeFileThresholdMb, Value = "25" },
                new AppSetting { Key = SettingKeys.AlertOnHighRisk, Value = "true" },
                new AppSetting { Key = SettingKeys.AlertOnCriticalRisk, Value = "true" },
                new AppSetting { Key = SettingKeys.DefaultTheme, Value = "dark" }
            );
        }
    }
}

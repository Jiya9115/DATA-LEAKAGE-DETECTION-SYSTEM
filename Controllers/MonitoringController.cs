using DLDS.Data;
using DLDS.Models;
using DLDS.Services;
using DLDS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DLDS.Controllers
{
    public class FileMetadataEntry
    {
        public string UploadedBy { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }

    [Authorize]
    public class MonitoringController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IFileMonitoringControl _monitoring;
        private readonly IRiskAssessmentService _risk;
        private readonly IAlertService _alerts;
        private readonly ILogger<MonitoringController> _logger;

        public MonitoringController(
            ApplicationDbContext db,
            IFileMonitoringControl monitoring,
            IRiskAssessmentService risk,
            IAlertService alerts,
            ILogger<MonitoringController> logger)
        {
            _db = db;
            _monitoring = monitoring;
            _risk = risk;
            _alerts = alerts;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            bool isAdmin = User.IsInRole(Roles.Administrator);
            var currentUsername = User.Identity?.Name ?? "";

            var folders = await _db.MonitoredFolders.AsNoTracking().ToListAsync();

            var eventsQuery = _db.FileEvents.AsNoTracking();
            if (!isAdmin)
            {
                eventsQuery = eventsQuery.Where(e => e.UploadedBy == currentUsername || (e.UploadedBy == null && e.IsSampleData));
            }

            var eventCounts = await eventsQuery
                .GroupBy(e => e.MonitoredFolderId)
                .Select(g => new { FolderId = g.Key, Total = g.Count(), Risk = g.Count(e => e.RiskLevel == RiskLevel.High || e.RiskLevel == RiskLevel.Critical), Last = g.Max(e => e.EventTimestamp) })
                .ToListAsync();

            var vm = new MonitoringViewModel
            {
                IsAdmin = isAdmin,
                Folders = folders.Select(f =>
                {
                    var stats = eventCounts.FirstOrDefault(x => x.FolderId == f.Id);
                    bool pathExists = Directory.Exists(f.FolderPath);
                    if (!pathExists && (f.FolderPath.Contains("App_Data") || f.FolderPath.Contains("Documents_Security")))
                    {
                        try { Directory.CreateDirectory(f.FolderPath); pathExists = true; } catch { }
                    }

                    int fileCount = 0;
                    int userFileCount = 0;
                    if (pathExists)
                    {
                        try
                        {
                            var meta = LoadFolderMetadata(f.FolderPath);
                            var allFiles = Directory.EnumerateFiles(f.FolderPath, "*", SearchOption.AllDirectories)
                                .Where(fn => !Path.GetFileName(fn).StartsWith(".") && !fn.EndsWith(".dlds_metadata.json"))
                                .ToList();

                            fileCount = allFiles.Count;
                            userFileCount = allFiles.Count(fn =>
                            {
                                var name = Path.GetFileName(fn);
                                if (meta.TryGetValue(name, out var entry))
                                    return string.Equals(entry.UploadedBy, currentUsername, StringComparison.OrdinalIgnoreCase);
                                return false;
                            });
                        }
                        catch { }
                    }

                    return new MonitoredFolderCardViewModel
                    {
                        Id = f.Id,
                        FolderName = f.FolderName,
                        FolderPath = f.FolderPath,
                        IsActive = f.IsActive,
                        IsCurrentlyWatching = _monitoring.IsWatching(f.Id),
                        TotalEvents = stats?.Total ?? 0,
                        RiskEvents = stats?.Risk ?? 0,
                        FileCount = fileCount,
                        UserFileCount = userFileCount,
                        IsAdmin = isAdmin,
                        LastMonitoredAt = f.LastMonitoredAt,
                        LastEventAt = stats?.Last,
                        PathExists = pathExists
                    };
                }).OrderByDescending(f => f.IsActive).ThenBy(f => f.FolderName).ToList()
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var folder = await _db.MonitoredFolders.FindAsync(id);
            if (folder == null) return NotFound();

            bool isAdmin = User.IsInRole(Roles.Administrator);
            var currentUsername = User.Identity?.Name ?? "";

            bool pathExists = Directory.Exists(folder.FolderPath);
            if (!pathExists && (folder.FolderPath.Contains("App_Data") || folder.FolderPath.Contains("Documents_Security")))
            {
                try { Directory.CreateDirectory(folder.FolderPath); pathExists = true; } catch { }
            }

            var eventsQuery = _db.FileEvents.AsNoTracking().Where(e => e.MonitoredFolderId == id);
            if (!isAdmin)
            {
                eventsQuery = eventsQuery.Where(e => e.UploadedBy == currentUsername || (e.UploadedBy == null && e.IsSampleData));
            }
            var events = await eventsQuery.OrderByDescending(e => e.EventTimestamp).ToListAsync();

            var fileList = new List<FolderFileItemViewModel>();

            if (pathExists)
            {
                try
                {
                    var meta = LoadFolderMetadata(folder.FolderPath);
                    var dirInfo = new DirectoryInfo(folder.FolderPath);
                    var fsFiles = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                        .Where(f => !f.Name.StartsWith(".") && !f.Name.Equals(".dlds_metadata.json", StringComparison.OrdinalIgnoreCase) && !f.Attributes.HasFlag(FileAttributes.Hidden))
                        .OrderByDescending(f => f.LastWriteTimeUtc);

                    foreach (var file in fsFiles)
                    {
                        var relPath = Path.GetRelativePath(folder.FolderPath, file.FullName);
                        var matchingEvents = events.Where(e => e.FileName.Equals(file.Name, StringComparison.OrdinalIgnoreCase)
                                                            || e.FilePath.EndsWith(relPath)).ToList();
                        var latestEvent = matchingEvents.FirstOrDefault();

                        string uploader = "";
                        if (meta.TryGetValue(file.Name, out var entry) && !string.IsNullOrEmpty(entry.UploadedBy))
                        {
                            uploader = entry.UploadedBy;
                        }
                        else if (latestEvent != null && !string.IsNullOrEmpty(latestEvent.UploadedBy))
                        {
                            uploader = latestEvent.UploadedBy;
                        }
                        else
                        {
                            uploader = "admin@dlds.local";
                        }

                        // RBAC FILTER: Regular users ONLY see files they uploaded
                        if (!isAdmin && !string.Equals(uploader, currentUsername, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        fileList.Add(new FolderFileItemViewModel
                        {
                            FileName = file.Name,
                            RelativePath = relPath,
                            FullPath = file.FullName,
                            FileSizeBytes = file.Length,
                            FileSizeFormatted = FormatBytes(file.Length),
                            Extension = file.Extension,
                            LastModified = file.LastWriteTimeUtc,
                            LatestRiskLevel = latestEvent?.RiskLevel,
                            LatestRiskScore = latestEvent?.RiskScore,
                            LatestEventType = latestEvent?.EventType,
                            LatestDescription = latestEvent?.Description,
                            EventCount = matchingEvents.Count,
                            UploadedBy = uploader,
                            CanManage = isAdmin || string.Equals(uploader, currentUsername, StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading files in folder {Path}", folder.FolderPath);
                }
            }

            var vm = new FolderDetailsViewModel
            {
                FolderId = folder.Id,
                FolderName = folder.FolderName,
                FolderPath = folder.FolderPath,
                IsActive = folder.IsActive,
                IsCurrentlyWatching = _monitoring.IsWatching(folder.Id),
                PathExists = pathExists,
                TotalEvents = events.Count,
                RiskEvents = events.Count(e => e.RiskLevel == RiskLevel.High || e.RiskLevel == RiskLevel.Critical),
                CreatedAt = folder.CreatedAt,
                LastMonitoredAt = folder.LastMonitoredAt,
                IsAdmin = isAdmin,
                CurrentUser = currentUsername,
                Files = fileList,
                RecentEvents = events.Take(30).ToList()
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(int id, List<IFormFile> files)
        {
            var folder = await _db.MonitoredFolders.FindAsync(id);
            if (folder == null) return NotFound();

            if (files == null || files.Count == 0)
            {
                TempData["Error"] = "Please select at least one file from your Mac to upload.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (!Directory.Exists(folder.FolderPath))
            {
                Directory.CreateDirectory(folder.FolderPath);
            }

            var meta = LoadFolderMetadata(folder.FolderPath);
            int processed = 0;
            var username = User.Identity?.Name ?? "system";
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            foreach (var formFile in files)
            {
                if (formFile.Length == 0) continue;

                var safeName = Path.GetFileName(formFile.FileName);
                var targetPath = Path.Combine(folder.FolderPath, safeName);
                bool isModify = System.IO.File.Exists(targetPath);
                var eventType = isModify ? EventType.Modified : EventType.Created;

                // Check ownership if replacing existing file
                bool isAdmin = User.IsInRole(Roles.Administrator);
                if (isModify && !isAdmin && meta.TryGetValue(safeName, out var existingMeta) && !string.Equals(existingMeta.UploadedBy, username, StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Error"] = $"A file named \"{safeName}\" already exists and was uploaded by another user.";
                    continue;
                }

                // Mark processed so background watcher does not duplicate event
                _monitoring.MarkEventProcessed(folder.Id, targetPath, eventType);

                using (var stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                {
                    await formFile.CopyToAsync(stream);
                }

                var assessment = await _risk.AssessAsync(
                    safeName,
                    targetPath,
                    eventType,
                    formFile.Length,
                    folder.Id);

                var fileEvent = new FileEvent
                {
                    FileName = safeName,
                    FilePath = targetPath,
                    EventType = eventType,
                    EventTimestamp = DateTime.UtcNow,
                    FileExtension = Path.GetExtension(safeName),
                    FileSize = formFile.Length,
                    RiskLevel = assessment.RiskLevel,
                    RiskScore = assessment.RiskScore,
                    Description = assessment.Reason,
                    MonitoredFolderId = folder.Id,
                    UploadedBy = username
                };

                _db.FileEvents.Add(fileEvent);
                await _db.SaveChangesAsync();

                // Save ownership metadata
                meta[safeName] = new FileMetadataEntry
                {
                    UploadedBy = username,
                    UploadedAt = DateTime.UtcNow
                };

                await _alerts.CreateAlertIfNeededAsync(fileEvent, assessment.Reason);

                var actionName = isModify ? "FileModified" : "FileUploaded";
                var auditDesc = $"User '{username}' {(isModify ? "modified (re-uploaded)" : "uploaded")} file '{safeName}' ({FormatBytes(formFile.Length)}) to folder '{folder.FolderName}'. Risk: {assessment.RiskLevel} (Score: {assessment.RiskScore}) — {assessment.Reason}";

                _db.AuditLogs.Add(new AuditLog
                {
                    Action = actionName,
                    Description = auditDesc,
                    Username = username,
                    IPAddress = ipAddress,
                    Timestamp = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();

                processed++;
            }

            SaveFolderMetadata(folder.FolderPath, meta);

            folder.LastMonitoredAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Successfully uploaded and processed {processed} file(s) from your Mac. Detection events, risk score, and audit logs recorded.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ModifyFile(int id, string fileName, string? textContent, IFormFile? replacementFile)
        {
            var folder = await _db.MonitoredFolders.FindAsync(id);
            if (folder == null) return NotFound();

            var safeName = Path.GetFileName(fileName);
            var targetPath = Path.Combine(folder.FolderPath, safeName);

            if (!System.IO.File.Exists(targetPath))
            {
                TempData["Error"] = $"File \"{safeName}\" not found in monitored folder.";
                return RedirectToAction(nameof(Details), new { id });
            }

            bool isAdmin = User.IsInRole(Roles.Administrator);
            var currentUsername = User.Identity?.Name ?? "system";
            var meta = LoadFolderMetadata(folder.FolderPath);

            // Ownership check
            if (!isAdmin && meta.TryGetValue(safeName, out var entry) && !string.Equals(entry.UploadedBy, currentUsername, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            _monitoring.MarkEventProcessed(folder.Id, targetPath, EventType.Modified);

            long newSize = 0;
            if (replacementFile != null && replacementFile.Length > 0)
            {
                using (var stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                {
                    await replacementFile.CopyToAsync(stream);
                }
                newSize = replacementFile.Length;
            }
            else if (textContent != null)
            {
                await System.IO.File.WriteAllTextAsync(targetPath, textContent);
                newSize = new FileInfo(targetPath).Length;
            }
            else
            {
                // Append modification timestamp to simulate modification
                await System.IO.File.AppendAllTextAsync(targetPath, $"\n# Modified by {currentUsername} at {DateTime.UtcNow:u}\n");
                newSize = new FileInfo(targetPath).Length;
            }

            var assessment = await _risk.AssessAsync(
                safeName,
                targetPath,
                EventType.Modified,
                newSize,
                folder.Id);

            var fileEvent = new FileEvent
            {
                FileName = safeName,
                FilePath = targetPath,
                EventType = EventType.Modified,
                EventTimestamp = DateTime.UtcNow,
                FileExtension = Path.GetExtension(safeName),
                FileSize = newSize,
                RiskLevel = assessment.RiskLevel,
                RiskScore = assessment.RiskScore,
                Description = assessment.Reason,
                MonitoredFolderId = folder.Id,
                UploadedBy = currentUsername
            };

            _db.FileEvents.Add(fileEvent);
            await _db.SaveChangesAsync();

            await _alerts.CreateAlertIfNeededAsync(fileEvent, assessment.Reason);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "FileModified",
                Description = $"User '{currentUsername}' modified file '{safeName}' ({FormatBytes(newSize)}) in folder '{folder.FolderName}'. Risk: {assessment.RiskLevel} (Score: {assessment.RiskScore}) — {assessment.Reason}",
                Username = currentUsername,
                IPAddress = ipAddress,
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            folder.LastMonitoredAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            TempData["Success"] = $"File \"{safeName}\" modified. Risk score and audit log recorded.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFile(int id, string fileName)
        {
            var folder = await _db.MonitoredFolders.FindAsync(id);
            if (folder == null) return NotFound();

            var safeName = Path.GetFileName(fileName);
            var targetPath = Path.Combine(folder.FolderPath, safeName);

            bool isAdmin = User.IsInRole(Roles.Administrator);
            var currentUsername = User.Identity?.Name ?? "system";
            var meta = LoadFolderMetadata(folder.FolderPath);

            // Ownership check
            if (!isAdmin && meta.TryGetValue(safeName, out var entry) && !string.Equals(entry.UploadedBy, currentUsername, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            if (System.IO.File.Exists(targetPath))
            {
                _monitoring.MarkEventProcessed(folder.Id, targetPath, EventType.Deleted);
                System.IO.File.Delete(targetPath);
            }

            meta.Remove(safeName);
            SaveFolderMetadata(folder.FolderPath, meta);

            var assessment = await _risk.AssessAsync(
                safeName,
                targetPath,
                EventType.Deleted,
                null,
                folder.Id);

            var fileEvent = new FileEvent
            {
                FileName = safeName,
                FilePath = targetPath,
                EventType = EventType.Deleted,
                EventTimestamp = DateTime.UtcNow,
                FileExtension = Path.GetExtension(safeName),
                FileSize = null,
                RiskLevel = assessment.RiskLevel,
                RiskScore = assessment.RiskScore,
                Description = assessment.Reason,
                MonitoredFolderId = folder.Id,
                UploadedBy = currentUsername
            };

            _db.FileEvents.Add(fileEvent);
            await _db.SaveChangesAsync();

            await _alerts.CreateAlertIfNeededAsync(fileEvent, assessment.Reason);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "FileDeleted",
                Description = $"User '{currentUsername}' deleted file '{safeName}' from folder '{folder.FolderName}'. Risk: {assessment.RiskLevel} (Score: {assessment.RiskScore}) — {assessment.Reason}",
                Username = currentUsername,
                IPAddress = ipAddress,
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            folder.LastMonitoredAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            TempData["Success"] = $"File \"{safeName}\" deleted. Risk score and audit log recorded.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpGet]
        public async Task<IActionResult> DownloadFile(int id, string fileName)
        {
            var folder = await _db.MonitoredFolders.FindAsync(id);
            if (folder == null) return NotFound();

            var safeName = Path.GetFileName(fileName);
            var targetPath = Path.Combine(folder.FolderPath, safeName);

            if (!System.IO.File.Exists(targetPath)) return NotFound();

            bool isAdmin = User.IsInRole(Roles.Administrator);
            var currentUsername = User.Identity?.Name ?? "system";
            var meta = LoadFolderMetadata(folder.FolderPath);

            if (!isAdmin && meta.TryGetValue(safeName, out var entry) && !string.Equals(entry.UploadedBy, currentUsername, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            return PhysicalFile(targetPath, "application/octet-stream", safeName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(AddFolderViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.FolderName))
            {
                TempData["Error"] = "Please provide a valid folder name.";
                return RedirectToAction(nameof(Index));
            }

            string path;
            if (model.UseManagedStorage || string.IsNullOrWhiteSpace(model.FolderPath))
            {
                var safeName = string.Join("_", model.FolderName.Split(Path.GetInvalidFileNameChars())).Trim();
                if (string.IsNullOrEmpty(safeName)) safeName = "Folder_" + Guid.NewGuid().ToString("N")[..6];
                path = Path.Combine(AppContext.BaseDirectory, "App_Data", "MonitoredFolders", safeName);
            }
            else
            {
                path = model.FolderPath.Trim();
            }

            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Unable to create or access folder \"{path}\": {ex.Message}";
                return RedirectToAction(nameof(Index));
            }

            if (await _db.MonitoredFolders.AnyAsync(f => f.FolderPath == path))
            {
                TempData["Error"] = "This folder path is already being monitored.";
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

            TempData["Success"] = $"Folder \"{folder.FolderName}\" added successfully. You can now upload files from your Mac.";
            return RedirectToAction(nameof(Details), new { id = folder.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Start(int id)
        {
            var folder = await _db.MonitoredFolders.FindAsync(id);
            if (folder == null) return NotFound();

            if (!Directory.Exists(folder.FolderPath))
            {
                try { Directory.CreateDirectory(folder.FolderPath); }
                catch
                {
                    TempData["Error"] = $"Cannot start monitoring — folder \"{folder.FolderPath}\" cannot be created or accessed.";
                    return RedirectToAction(nameof(Index));
                }
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
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Timestamp = DateTime.UtcNow
            });
            await Task.CompletedTask;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int i = 0;
            double d = bytes;
            while (d >= 1024 && i < suffixes.Length - 1)
            {
                d /= 1024;
                i++;
            }
            return $"{d:0.##} {suffixes[i]}";
        }

        private static string GetMetadataFilePath(string folderPath) =>
            Path.Combine(folderPath, ".dlds_metadata.json");

        private static Dictionary<string, FileMetadataEntry> LoadFolderMetadata(string folderPath)
        {
            try
            {
                var metaFile = GetMetadataFilePath(folderPath);
                if (System.IO.File.Exists(metaFile))
                {
                    var json = System.IO.File.ReadAllText(metaFile);
                    return JsonSerializer.Deserialize<Dictionary<string, FileMetadataEntry>>(json) 
                           ?? new Dictionary<string, FileMetadataEntry>(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { }
            return new Dictionary<string, FileMetadataEntry>(StringComparer.OrdinalIgnoreCase);
        }

        private static void SaveFolderMetadata(string folderPath, Dictionary<string, FileMetadataEntry> metadata)
        {
            try
            {
                var metaFile = GetMetadataFilePath(folderPath);
                var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(metaFile, json);
            }
            catch { }
        }
    }
}

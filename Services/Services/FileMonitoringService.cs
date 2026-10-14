using System.Collections.Concurrent;
using DLDS.Data;
using DLDS.Models;
using Microsoft.EntityFrameworkCore;

namespace DLDS.Services
{
    /// <summary>
    /// Public control surface used by controllers to start/stop watching a folder
    /// without waiting for the background sync loop.
    /// </summary>
    public interface IFileMonitoringControl
    {
        Task StartWatchingAsync(int monitoredFolderId, CancellationToken ct = default);
        Task StopWatchingAsync(int monitoredFolderId, CancellationToken ct = default);
        bool IsWatching(int monitoredFolderId);
    }

    /// <summary>
    /// Hosted background service that owns one FileSystemWatcher per active MonitoredFolder.
    /// Runs on its own thread pool work and never blocks the web request pipeline.
    ///
    /// Responsibilities:
    ///  - Reconcile watchers against MonitoredFolders.IsActive every few seconds.
    ///  - Debounce duplicate FileSystemWatcher events (a single save can fire Changed twice).
    ///  - Persist a FileEvent + run risk assessment + raise an Alert for every real event.
    ///  - Never crash the app if a folder disappears or becomes inaccessible.
    /// </summary>
    public class FileMonitoringService : BackgroundService, IFileMonitoringControl
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<FileMonitoringService> _logger;

        private readonly ConcurrentDictionary<int, FileSystemWatcher> _watchers = new();
        // Debounce key: folderId|fullPath|eventType -> last-seen UTC ticks
        private readonly ConcurrentDictionary<string, DateTime> _recentEvents = new();
        private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(750);

        private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(5);

        public FileMonitoringService(IServiceScopeFactory scopeFactory, ILogger<FileMonitoringService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public bool IsWatching(int monitoredFolderId) => _watchers.ContainsKey(monitoredFolderId);

        public async Task StartWatchingAsync(int monitoredFolderId, CancellationToken ct = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var folder = await db.MonitoredFolders.FindAsync(new object?[] { monitoredFolderId }, ct);
            if (folder == null) return;

            folder.IsActive = true;
            folder.LastMonitoredAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            AttachWatcher(folder);
        }

        public Task StopWatchingAsync(int monitoredFolderId, CancellationToken ct = default)
        {
            DetachWatcher(monitoredFolderId);

            return Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var folder = await db.MonitoredFolders.FindAsync(new object?[] { monitoredFolderId }, ct);
                if (folder != null)
                {
                    folder.IsActive = false;
                    await db.SaveChangesAsync(ct);
                }
            }, ct);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Startup delay so the app finishes booting/migrating before we touch the DB.
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ReconcileWatchersAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    // Reconciliation must never take the whole app down.
                    _logger.LogError(ex, "Error while reconciling folder watchers.");
                }

                try { await Task.Delay(ReconcileInterval, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }

            foreach (var id in _watchers.Keys.ToList())
                DetachWatcher(id);
        }

        private async Task ReconcileWatchersAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var activeFolders = await db.MonitoredFolders
                .Where(f => f.IsActive)
                .ToListAsync(ct);

            var activeIds = activeFolders.Select(f => f.Id).ToHashSet();

            // Stop watchers whose folder was deactivated or deleted.
            foreach (var watchedId in _watchers.Keys.ToList())
            {
                if (!activeIds.Contains(watchedId))
                    DetachWatcher(watchedId);
            }

            // Start watchers for newly-activated folders.
            foreach (var folder in activeFolders)
            {
                if (_watchers.ContainsKey(folder.Id)) continue;
                AttachWatcher(folder);
            }
        }

        private void AttachWatcher(MonitoredFolder folder)
        {
            if (_watchers.ContainsKey(folder.Id)) return;

            if (!Directory.Exists(folder.FolderPath))
            {
                _logger.LogWarning("Cannot monitor folder {Path} — it does not exist or is inaccessible.", folder.FolderPath);
                return;
            }

            try
            {
                var watcher = new FileSystemWatcher(folder.FolderPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Size
                                 | NotifyFilters.CreationTime
                };

                watcher.Created += (s, e) => OnFsEvent(folder.Id, e.FullPath, EventType.Created);
                watcher.Deleted += (s, e) => OnFsEvent(folder.Id, e.FullPath, EventType.Deleted);
                watcher.Changed += (s, e) => OnFsEvent(folder.Id, e.FullPath, EventType.Modified);
                watcher.Renamed += (s, e) => OnFsEvent(folder.Id, e.FullPath, EventType.Renamed);
                watcher.Error += (s, e) => OnWatcherError(folder.Id, e.GetException());

                watcher.EnableRaisingEvents = true;

                _watchers[folder.Id] = watcher;
                _logger.LogInformation("Started monitoring folder {Path} (Id={Id}).", folder.FolderPath, folder.Id);
            }
            catch (Exception ex)
            {
                // Permission denied, path too long, etc. — log and move on, do not crash.
                _logger.LogError(ex, "Failed to start watcher for folder {Path}.", folder.FolderPath);
            }
        }

        private void DetachWatcher(int folderId)
        {
            if (_watchers.TryRemove(folderId, out var watcher))
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                    _logger.LogInformation("Stopped monitoring folder Id={Id}.", folderId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing watcher for folder Id={Id}.", folderId);
                }
            }
        }

        private void OnWatcherError(int folderId, Exception? ex)
        {
            // Common cause: the watched directory itself was deleted/unmounted.
            _logger.LogWarning(ex, "FileSystemWatcher error for folder Id={Id}. Detaching watcher.", folderId);
            DetachWatcher(folderId);
        }

        /// <summary>
        /// Fired on a background thread by FileSystemWatcher. Debounces duplicate
        /// notifications, then hands off to async processing without blocking the watcher.
        /// </summary>
        private void OnFsEvent(int folderId, string fullPath, EventType eventType)
        {
            var key = $"{folderId}|{fullPath}|{eventType}";
            var now = DateTime.UtcNow;

            if (_recentEvents.TryGetValue(key, out var last) && now - last < DebounceWindow)
                return; // duplicate notification within debounce window — ignore

            _recentEvents[key] = now;

            // Opportunistic cleanup so the dictionary doesn't grow unbounded.
            if (_recentEvents.Count > 5000)
            {
                foreach (var oldKey in _recentEvents.Where(kv => now - kv.Value > TimeSpan.FromMinutes(5)).Select(kv => kv.Key).ToList())
                    _recentEvents.TryRemove(oldKey, out _);
            }

            _ = ProcessEventAsync(folderId, fullPath, eventType);
        }

        private async Task ProcessEventAsync(int folderId, string fullPath, EventType eventType)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var risk = scope.ServiceProvider.GetRequiredService<IRiskAssessmentService>();
                var alerts = scope.ServiceProvider.GetRequiredService<IAlertService>();

                var fileName = Path.GetFileName(fullPath);
                var extension = Path.GetExtension(fullPath);
                long? fileSize = null;

                if (eventType != EventType.Deleted)
                {
                    try
                    {
                        if (File.Exists(fullPath))
                            fileSize = new FileInfo(fullPath).Length;
                    }
                    catch (IOException)
                    {
                        // File was locked/removed between the notification and our read — skip size, keep going.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // No permission to stat the file — record the event anyway, just without a size.
                    }
                }

                var assessment = await risk.AssessAsync(fileName, fullPath, eventType, fileSize, folderId);

                var fileEvent = new FileEvent
                {
                    FileName = fileName,
                    FilePath = fullPath,
                    EventType = eventType,
                    EventTimestamp = DateTime.UtcNow,
                    FileExtension = extension,
                    FileSize = fileSize,
                    RiskLevel = assessment.RiskLevel,
                    RiskScore = assessment.RiskScore,
                    Description = assessment.Reason,
                    MonitoredFolderId = folderId
                };

                db.FileEvents.Add(fileEvent);
                await db.SaveChangesAsync();

                await alerts.CreateAlertIfNeededAsync(fileEvent, assessment.Reason);
            }
            catch (Exception ex)
            {
                // A single failed event must never crash the monitoring loop.
                _logger.LogError(ex, "Failed to process file event for {Path} ({EventType}).", fullPath, eventType);
            }
        }
    }
}

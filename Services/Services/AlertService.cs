using DLDS.Data;
using DLDS.Models;

namespace DLDS.Services
{
    public interface IAlertService
    {
        Task<Alert?> CreateAlertIfNeededAsync(FileEvent fileEvent, string reason, CancellationToken ct = default);
    }

    /// <summary>
    /// Automatically raises an Alert record for High/Critical risk FileEvents.
    /// Low/Medium events are recorded in FileEvents but do not generate alerts,
    /// keeping the Alert Center focused on activity that needs review.
    /// </summary>
    public class AlertService : IAlertService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AlertService> _logger;

        public AlertService(ApplicationDbContext db, ILogger<AlertService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Alert?> CreateAlertIfNeededAsync(FileEvent fileEvent, string reason, CancellationToken ct = default)
        {
            if (fileEvent.RiskLevel != RiskLevel.High && fileEvent.RiskLevel != RiskLevel.Critical)
                return null;

            var alert = new Alert
            {
                FileEventId = fileEvent.Id,
                Title = fileEvent.RiskLevel == RiskLevel.Critical
                    ? $"Critical: {fileEvent.EventType} — {fileEvent.FileName}"
                    : $"High risk: {fileEvent.EventType} — {fileEvent.FileName}",
                Message = reason,
                RiskLevel = fileEvent.RiskLevel,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            _db.Alerts.Add(alert);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save alert for FileEvent {FileEventId}", fileEvent.Id);
                return null;
            }

            return alert;
        }
    }
}

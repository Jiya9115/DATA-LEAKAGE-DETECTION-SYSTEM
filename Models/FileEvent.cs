using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DLDS.Models
{
    public enum EventType
    {
        Created,
        Deleted,
        Modified,
        Renamed
    }

    public enum RiskLevel
    {
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// Represents a single detected file-system event captured by the
    /// FileMonitoringService and scored by the RiskAssessmentService.
    /// </summary>
    public class FileEvent
    {
        public int Id { get; set; }

        [Required, MaxLength(260)]
        public string FileName { get; set; } = string.Empty;

        [Required, MaxLength(1024)]
        public string FilePath { get; set; } = string.Empty;

        [Required]
        public EventType EventType { get; set; }

        public DateTime EventTimestamp { get; set; } = DateTime.UtcNow;

        [MaxLength(20)]
        public string FileExtension { get; set; } = string.Empty;

        /// <summary>File size in bytes at the time the event was recorded. Null if the file no longer exists (e.g. deletions).</summary>
        public long? FileSize { get; set; }

        public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;

        /// <summary>Numeric risk score from 0-100, produced by RiskAssessmentService.</summary>
        public int RiskScore { get; set; }

        [MaxLength(1000)]
        public string Description { get; set; } = string.Empty;

        public int MonitoredFolderId { get; set; }

        [ForeignKey(nameof(MonitoredFolderId))]
        public MonitoredFolder? MonitoredFolder { get; set; }

        /// <summary>Whether this row was inserted by DbInitializer for demo purposes.</summary>
        public bool IsSampleData { get; set; } = false;
    }
}

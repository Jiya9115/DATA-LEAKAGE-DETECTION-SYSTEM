using System.ComponentModel.DataAnnotations;

namespace DLDS.Models
{
    /// <summary>
    /// An administrator-configurable rule used to score file events.
    /// A rule matches on EventType (optional) and/or FileExtension (optional).
    /// The most specific matching active rule with the highest RiskScore wins;
    /// see RiskAssessmentService for exact evaluation order.
    /// </summary>
    public class RiskRule
    {
        public int Id { get; set; }

        [Required, MaxLength(150)]
        public string RuleName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        /// <summary>Null/empty means "matches any event type".</summary>
        public EventType? EventType { get; set; }

        /// <summary>Null/empty means "matches any extension". Stored with leading dot, e.g. ".sql".</summary>
        [MaxLength(20)]
        public string? FileExtension { get; set; }

        /// <summary>Optional minimum file size (bytes) required for this rule to apply.</summary>
        public long? MinimumFileSizeBytes { get; set; }

        [Range(0, 100)]
        public int RiskScore { get; set; }

        public RiskLevel RiskLevel { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsSampleData { get; set; } = false;
    }
}

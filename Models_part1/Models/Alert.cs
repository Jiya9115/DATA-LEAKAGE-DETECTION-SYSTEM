using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DLDS.Models
{
    /// <summary>
    /// A security alert auto-generated for High/Critical risk FileEvents.
    /// </summary>
    public class Alert
    {
        public int Id { get; set; }

        public int FileEventId { get; set; }

        [ForeignKey(nameof(FileEventId))]
        public FileEvent? FileEvent { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required, MaxLength(1000)]
        public string Message { get; set; } = string.Empty;

        public RiskLevel RiskLevel { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsSampleData { get; set; } = false;
    }
}

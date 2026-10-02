using System.ComponentModel.DataAnnotations;

namespace DLDS.Models
{
    /// <summary>
    /// Records important application actions (login, folder changes, rule edits, etc.)
    /// for the audit trail / timeline view.
    /// </summary>
    public class AuditLog
    {
        public int Id { get; set; }

        [Required, MaxLength(150)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string Description { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [MaxLength(256)]
        public string Username { get; set; } = "system";

        [MaxLength(64)]
        public string IPAddress { get; set; } = string.Empty;

        public bool IsSampleData { get; set; } = false;
    }
}

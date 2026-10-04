using System.ComponentModel.DataAnnotations;

namespace DLDS.Models
{
    /// <summary>
    /// A folder the user has configured for real-time monitoring.
    /// </summary>
    public class MonitoredFolder
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string FolderName { get; set; } = string.Empty;

        [Required, MaxLength(1024)]
        public string FolderPath { get; set; } = string.Empty;

        /// <summary>Whether the watcher for this folder should currently be running.</summary>
        public bool IsActive { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastMonitoredAt { get; set; }

        public bool IsSampleData { get; set; } = false;

        public ICollection<FileEvent> FileEvents { get; set; } = new List<FileEvent>();
    }
}

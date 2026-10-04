using System.ComponentModel.DataAnnotations;

namespace DLDS.ViewModels
{
    public class MonitoringViewModel
    {
        public List<MonitoredFolderCardViewModel> Folders { get; set; } = new();
        public AddFolderViewModel NewFolder { get; set; } = new();
    }

    public class MonitoredFolderCardViewModel
    {
        public int Id { get; set; }
        public string FolderName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsCurrentlyWatching { get; set; }
        public int TotalEvents { get; set; }
        public int RiskEvents { get; set; }
        public DateTime? LastMonitoredAt { get; set; }
        public DateTime? LastEventAt { get; set; }
        public bool PathExists { get; set; }
    }

    public class AddFolderViewModel
    {
        [Required(ErrorMessage = "Please give this folder a display name.")]
        [MaxLength(200)]
        public string FolderName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter a folder path to monitor.")]
        [MaxLength(1024)]
        public string FolderPath { get; set; } = string.Empty;

        public bool StartImmediately { get; set; } = true;
    }
}

using System.ComponentModel.DataAnnotations;
using DLDS.Models;

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
        public int FileCount { get; set; }
        public DateTime? LastMonitoredAt { get; set; }
        public DateTime? LastEventAt { get; set; }
        public bool PathExists { get; set; }
    }

    public class AddFolderViewModel
    {
        [Required(ErrorMessage = "Please give this folder a display name.")]
        [MaxLength(200)]
        public string FolderName { get; set; } = string.Empty;

        [MaxLength(1024)]
        public string? FolderPath { get; set; }

        public bool UseManagedStorage { get; set; } = true;

        public bool StartImmediately { get; set; } = true;
    }

    public class FolderDetailsViewModel
    {
        public int FolderId { get; set; }
        public string FolderName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsCurrentlyWatching { get; set; }
        public bool PathExists { get; set; }
        public int TotalEvents { get; set; }
        public int RiskEvents { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastMonitoredAt { get; set; }
        public List<FolderFileItemViewModel> Files { get; set; } = new();
        public List<FileEvent> RecentEvents { get; set; } = new();
    }

    public class FolderFileItemViewModel
    {
        public string FileName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string FileSizeFormatted { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        public RiskLevel? LatestRiskLevel { get; set; }
        public int? LatestRiskScore { get; set; }
        public EventType? LatestEventType { get; set; }
        public string? LatestDescription { get; set; }
        public int EventCount { get; set; }
    }
}

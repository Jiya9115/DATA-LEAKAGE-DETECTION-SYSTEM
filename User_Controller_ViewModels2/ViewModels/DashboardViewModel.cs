using DLDS.Models;

namespace DLDS.ViewModels
{
    public class DashboardViewModel
    {
        public int TotalEvents { get; set; }
        public int FilesCreated { get; set; }
        public int FilesModified { get; set; }
        public int FilesDeleted { get; set; }
        public int FilesRenamed { get; set; }
        public int HighRiskEvents { get; set; }
        public int CriticalAlerts { get; set; }
        public int ActiveMonitoredFolders { get; set; }
        public int TotalMonitoredFolders { get; set; }

        public Dictionary<string, int> EventsByType { get; set; } = new();
        public Dictionary<string, int> EventsByRiskLevel { get; set; } = new();
        public List<(string Label, int Count)> EventsOverTime { get; set; } = new();
        public Dictionary<string, int> TopExtensions { get; set; } = new();

        public List<FileEvent> RecentEvents { get; set; } = new();
        public List<Alert> RecentAlerts { get; set; } = new();

        public bool HasAnyData => TotalEvents > 0;
    }
}

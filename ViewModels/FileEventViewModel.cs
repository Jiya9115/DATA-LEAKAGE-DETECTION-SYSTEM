using DLDS.Models;

namespace DLDS.ViewModels
{
    public class FileEventListViewModel
    {
        public List<FileEvent> Events { get; set; } = new();
        public List<MonitoredFolder> Folders { get; set; } = new();

        public string? Search { get; set; }
        public EventType? EventType { get; set; }
        public RiskLevel? RiskLevel { get; set; }
        public int? MonitoredFolderId { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }

        public string SortBy { get; set; } = "EventTimestamp";
        public bool SortDescending { get; set; } = true;

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int TotalCount { get; set; }
        public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    }
}

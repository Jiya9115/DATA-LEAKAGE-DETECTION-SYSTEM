using DLDS.Models;
using DLDS.Services;

namespace DLDS.ViewModels
{
    public class ReportPageViewModel
    {
        public ReportFilter Filter { get; set; } = new();
        public ReportData? Report { get; set; }
        public List<MonitoredFolder> Folders { get; set; } = new();
        public string DateRangePreset { get; set; } = "last7";
    }
}

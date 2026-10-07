namespace DLDS.ViewModels
{
    public class SettingsViewModel
    {
        public string SensitiveExtensions { get; set; } = string.Empty;
        public int LargeFileThresholdMb { get; set; } = 25;
        public bool AlertOnHighRisk { get; set; } = true;
        public bool AlertOnCriticalRisk { get; set; } = true;
        public string DefaultTheme { get; set; } = "dark";
    }
}

using System.ComponentModel.DataAnnotations;

namespace DLDS.Models
{
    /// <summary>
    /// Simple persisted key/value store backing the Settings page
    /// (alert preferences, sensitive-extension list overrides, etc.)
    /// so administrators can change behavior without redeploying.
    /// </summary>
    public class AppSetting
    {
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Key { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string Value { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Well-known setting keys used across the app.</summary>
    public static class SettingKeys
    {
        public const string SensitiveExtensions = "SensitiveExtensions";       // comma-separated, e.g. ".pdf,.docx"
        public const string LargeFileThresholdMb = "LargeFileThresholdMb";     // integer
        public const string AlertOnHighRisk = "AlertOnHighRisk";               // "true"/"false"
        public const string AlertOnCriticalRisk = "AlertOnCriticalRisk";       // "true"/"false"
        public const string DefaultTheme = "DefaultTheme";                     // "dark"/"light"
    }
}

using System.ComponentModel.DataAnnotations;
using DLDS.Models;

namespace DLDS.ViewModels
{
    public class RiskRuleFormViewModel
    {
        public int Id { get; set; }

        [Required, MaxLength(150)]
        public string RuleName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public EventType? EventType { get; set; }

        [MaxLength(20)]
        public string? FileExtension { get; set; }

        [Range(0, long.MaxValue)]
        public long? MinimumFileSizeMb { get; set; }

        [Range(0, 100)]
        public int RiskScore { get; set; } = 50;

        public RiskLevel RiskLevel { get; set; } = RiskLevel.Medium;

        public bool IsActive { get; set; } = true;
    }
}

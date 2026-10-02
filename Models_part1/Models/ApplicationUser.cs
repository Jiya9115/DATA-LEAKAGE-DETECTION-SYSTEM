using Microsoft.AspNetCore.Identity;

namespace DLDS.Models
{
    /// <summary>
    /// Extends ASP.NET Core Identity's user with DLDS-specific display fields.
    /// Roles ("Administrator", "User") are managed via ASP.NET Core Identity roles,
    /// not a field on this class, so role checks stay consistent with [Authorize(Roles = "...")].
    /// </summary>
    public class ApplicationUser : IdentityUser
    {
        public string DisplayName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastLoginAt { get; set; }
    }

    public static class Roles
    {
        public const string Administrator = "Administrator";
        public const string User = "User";
    }
}

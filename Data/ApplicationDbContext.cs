using DLDS.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DLDS.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<FileEvent> FileEvents => Set<FileEvent>();
        public DbSet<MonitoredFolder> MonitoredFolders => Set<MonitoredFolder>();
        public DbSet<Alert> Alerts => Set<Alert>();
        public DbSet<RiskRule> RiskRules => Set<RiskRule>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<AppSetting> AppSettings => Set<AppSetting>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<FileEvent>(e =>
            {
                e.HasIndex(x => x.EventTimestamp);
                e.HasIndex(x => x.RiskLevel);
                e.HasIndex(x => x.EventType);
                e.Property(x => x.EventType).HasConversion<string>();
                e.Property(x => x.RiskLevel).HasConversion<string>();

                e.HasOne(x => x.MonitoredFolder)
                 .WithMany(f => f.FileEvents)
                 .HasForeignKey(x => x.MonitoredFolderId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<Alert>(e =>
            {
                e.HasIndex(x => x.IsRead);
                e.HasIndex(x => x.CreatedAt);
                e.Property(x => x.RiskLevel).HasConversion<string>();

                e.HasOne(x => x.FileEvent)
                 .WithMany()
                 .HasForeignKey(x => x.FileEventId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<RiskRule>(e =>
            {
                e.Property(x => x.EventType).HasConversion<string>();
                e.Property(x => x.RiskLevel).HasConversion<string>();
            });

            builder.Entity<MonitoredFolder>(e =>
            {
                e.HasIndex(x => x.FolderPath).IsUnique();
            });

            builder.Entity<AuditLog>(e =>
            {
                e.HasIndex(x => x.Timestamp);
            });

            builder.Entity<AppSetting>(e =>
            {
                e.HasIndex(x => x.Key).IsUnique();
            });
        }
    }
}

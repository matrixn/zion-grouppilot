using GroupPilot.Core;
using Microsoft.EntityFrameworkCore;

namespace GroupPilot.Infrastructure;

public sealed class PilotDb(DbContextOptions<PilotDb> options) : DbContext(options)
{
    public DbSet<SocialAccount> SocialAccounts => Set<SocialAccount>();
    public DbSet<SocialGroup> Groups => Set<SocialGroup>();
    public DbSet<GroupCategory> Categories => Set<GroupCategory>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostMedia> Media => Set<PostMedia>();
    public DbSet<PostVariant> Variants => Set<PostVariant>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignGroup> Targets => Set<CampaignGroup>();
    public DbSet<PublishingAttempt> Attempts => Set<PublishingAttempt>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Campaign>().Property(campaign => campaign.RunNumber).HasDefaultValue(1);
        model.Entity<PublishingAttempt>().Property(attempt => attempt.RunNumber).HasDefaultValue(1);
        model.Entity<Campaign>().Property(campaign => campaign.PauseSeconds).HasDefaultValue(120);
        model.Entity<SocialGroup>().HasIndex(group => group.Url).IsUnique();
        model.Entity<GroupCategory>().HasIndex(category => category.Name).IsUnique();
        model.Entity<CampaignGroup>().HasOne<SocialGroup>().WithMany()
            .HasForeignKey(target => target.SocialGroupId).OnDelete(DeleteBehavior.SetNull);
        model.Entity<CampaignGroup>().Property(target => target.Status).IsConcurrencyToken();
    }
}

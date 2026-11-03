using GroupPilot.Core;
using GroupPilot.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GroupPilot.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("grouppilot-test-").FullName;

    private Storage Open() => new(new Factory(new DbContextOptionsBuilder<PilotDb>()
        .UseSqlite($"Data Source={Path.Combine(folder, "test.db")};Pooling=False").Options));

    [Fact]
    public async Task MigrationAndDraftsSurviveReopening()
    {
        var storage = Open();
        await storage.InitializeAsync();
        var post = new Post { Title = "Original", Kind = PostKind.New, Body = "Hello", Media = [new() { Path = "image.png" }] };
        await storage.SavePostAsync(post);
        await storage.SaveGroupAsync(new SocialGroup
            { Name = "Example", Url = "https://facebook.com/groups/example/" }, "Restaurants");

        storage = Open();
        await storage.InitializeAsync();
        var reopened = await storage.QueryAsync(db => db.Posts.Include(item => item.Media).SingleAsync());
        Assert.Equal("image.png", Assert.Single(reopened.Media).Path);
        var group = await storage.QueryAsync(db => db.Groups.Include(item => item.Category).SingleAsync());
        Assert.Equal("Restaurants", group.Category?.Name);
        await storage.SavePostAsync(new Post { Id = reopened.Id, Title = "Updated", Kind = PostKind.New, Body = "Edited" });
        Assert.Empty(await storage.QueryAsync(db => db.Media.ToListAsync()));
        Assert.Equal("Updated", await storage.QueryAsync(db => db.Posts.Select(item => item.Title).SingleAsync()));
    }

    [Fact]
    public async Task ImportDeduplicatesAndCampaignHistorySurvivesGroupDeletion()
    {
        var storage = Open();
        await storage.InitializeAsync();
        var csv = Path.Combine(folder, "groups.csv");
        await File.WriteAllTextAsync(csv, "name,url\n\"Example, Inc.\",https://facebook.com/groups/example/\nDuplicate,https://m.facebook.com/groups/Example/\nInvalid,https://example.org\n");
        Assert.Equal((1, 2), await storage.ImportGroupsAsync(csv));
        Assert.Equal((0, 3), await storage.ImportGroupsAsync(csv));
        var group = await storage.QueryAsync(db => db.Groups.SingleAsync());
        Assert.Equal("Example, Inc.", group.Name);

        var post = new Post { Title = "Draft", Body = "Content", Kind = PostKind.New };
        await storage.SavePostAsync(post);
        var scheduled = new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.FromHours(2));
        await storage.SaveCampaignAsync("Campaign", post.Id, [group.Id], scheduled);
        var campaign = await Open().QueryAsync(db => db.Campaigns.Include(item => item.Targets).SingleAsync());
        Assert.Equal(scheduled, campaign.ScheduledFor);
        var target = Assert.Single(campaign.Targets);
        await storage.RecordAttemptAsync(target.Id, TargetStatus.Failed, "");
        await Open().RecordAttemptAsync(target.Id, TargetStatus.Published, "https://www.facebook.com/groups/example/posts/123/");
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.RecordAttemptAsync(target.Id, TargetStatus.Skipped, ""));
        await storage.SaveAsync(async db => db.Groups.Remove(await db.Groups.SingleAsync()));
        var history = await Open().QueryAsync(db => db.Attempts.Include(item => item.Target).OrderBy(item => item.Id).ToListAsync());
        Assert.Equal([TargetStatus.Failed, TargetStatus.Published], history.Select(item => item.Status));
        Assert.All(history, attempt =>
        {
            Assert.Null(attempt.Target.SocialGroupId);
            Assert.Equal("Example, Inc.", attempt.Target.GroupName);
        });
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private sealed class Factory(DbContextOptions<PilotDb> options) : IDbContextFactory<PilotDb>
    {
        public PilotDb CreateDbContext() => new(options);
    }
}

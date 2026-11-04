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

    [Fact]
    public async Task ReviewAndInterruptedTargetSurviveReopenAndModeration()
    {
        var storage = Open();
        await storage.InitializeAsync();
        var group = new SocialGroup { Name = "Group", Url = "https://facebook.com/groups/test/" };
        await storage.SaveGroupAsync(group, "");
        await storage.SaveGroupReviewAsync(group.Id, group.Url, GroupPostingAccess.RequiresApproval, "Review before posting");
        var post = new Post { Title = "Draft", Kind = PostKind.New, Body = "Test" };
        await storage.SavePostAsync(post);
        await storage.SaveCampaignAsync("Campaign", post.Id, [group.Id], null);
        var target = await storage.QueryAsync(db => db.Targets.SingleAsync());
        await storage.BeginTargetAsync(target.Id);

        storage = Open();
        await storage.InitializeAsync();
        var reviewed = await storage.QueryAsync(db => db.Groups.SingleAsync());
        Assert.Equal(GroupPostingAccess.RequiresApproval, reviewed.PostingAccess);
        Assert.Equal("Review before posting", reviewed.Rules);
        Assert.NotNull(reviewed.ReviewedAt);
        Assert.Equal(TargetStatus.InProgress, await storage.QueryAsync(db => db.Targets.Select(item => item.Status).SingleAsync()));
        Assert.Empty(await storage.QueryAsync(db => db.Attempts.ToListAsync()));

        await storage.RecordAttemptAsync(target.Id, TargetStatus.AwaitingApproval, "");
        await Assert.ThrowsAsync<ArgumentException>(() => storage.BeginTargetAsync(target.Id));
        await storage.RecordAttemptAsync(target.Id, TargetStatus.Published, "https://www.facebook.com/groups/test/posts/42/");
        Assert.Equal([TargetStatus.AwaitingApproval, TargetStatus.Published],
            await storage.QueryAsync(db => db.Attempts.OrderBy(item => item.Id).Select(item => item.Status).ToListAsync()));
    }

    [Fact]
    public async Task ChangingGroupUrlClearsReviewAndPreventsOpeningOldTarget()
    {
        var storage = Open();
        await storage.InitializeAsync();
        var group = new SocialGroup { Name = "Group", Url = "https://facebook.com/groups/test/" };
        await storage.SaveGroupAsync(group, "");
        var oldUrl = group.Url;
        await storage.SaveGroupReviewAsync(group.Id, oldUrl, GroupPostingAccess.Allowed, "Rules");
        var post = new Post { Title = "Draft", Kind = PostKind.New, Body = "Test" };
        await storage.SavePostAsync(post);
        await storage.SaveCampaignAsync("Campaign", post.Id, [group.Id], null);
        var target = await storage.QueryAsync(db => db.Targets.SingleAsync());

        group.Url = "https://facebook.com/groups/other/";
        await storage.SaveGroupAsync(group, "");
        var updated = await storage.QueryAsync(db => db.Groups.SingleAsync());
        Assert.Equal(GroupPostingAccess.Unreviewed, updated.PostingAccess);
        Assert.Null(updated.ReviewedAt);
        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveGroupReviewAsync(group.Id, oldUrl, GroupPostingAccess.Allowed, ""));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.BeginTargetAsync(target.Id));
    }

    [Fact]
    public async Task BrowserGroupImportPreservesExistingGroupsAndDeduplicates()
    {
        var storage = Open();
        await storage.InitializeAsync();
        var existing = new SocialGroup { Name = "My label", Url = "https://facebook.com/groups/existing/" };
        await storage.SaveGroupAsync(existing, "Favorites");
        var rows = new SocialGroup[]
        {
            new() { Name = "Facebook label", Url = "https://m.facebook.com/groups/Existing/" },
            new() { Name = "New group", Url = "https://facebook.com/groups/new-group/" },
            new() { Name = "Duplicate", Url = "https://m.facebook.com/groups/NEW-GROUP/?ref=groups" },
            new() { Name = "Invalid", Url = "https://example.org/groups/fake/" }
        };

        Assert.Equal((1, 3), await storage.ImportGroupsAsync(rows));
        Assert.Equal((0, 4), await storage.ImportGroupsAsync(rows));
        var saved = await storage.QueryAsync(db => db.Groups.Include(group => group.Category)
            .SingleAsync(group => group.Id == existing.Id));
        Assert.Equal("My label", saved.Name);
        Assert.Equal("Favorites", saved.Category?.Name);
    }

    [Fact]
    public async Task SyncUpdatesMetadataAndKeepsHistoryForRemovedGroup()
    {
        var storage = Open();
        await storage.InitializeAsync();
        var keep = new SocialGroup { Url = "https://facebook.com/groups/keep/", Name = "Local name" };
        var remove = new SocialGroup { Url = "https://facebook.com/groups/remove/", Name = "Remove" };
        await storage.SaveGroupAsync(keep, "Favorites");
        await storage.SaveGroupAsync(remove, "");
        var post = new Post { Title = "Draft", Body = "Test", Kind = PostKind.New };
        await storage.SavePostAsync(post);
        await storage.SaveCampaignAsync("Campaign", post.Id, [remove.Id], null);
        var target = await storage.QueryAsync(db => db.Targets.SingleAsync());
        await storage.RecordAttemptAsync(target.Id, TargetStatus.Failed, "");

        var scanned = new SocialGroup { Url = keep.Url, Name = "Remote name" };
        FacebookGroupMetadata.Apply(scanned, "Grup privat · 2,4 mii de membri · 10 posts a day");
        var added = new SocialGroup { Url = "https://facebook.com/groups/new/", Name = "New" };
        await Assert.ThrowsAsync<ArgumentException>(() => storage.SyncFacebookGroupsAsync([scanned], new Dictionary<int, string>()));
        Assert.Equal((1, 1, 1), await storage.SyncFacebookGroupsAsync([scanned, added],
            new Dictionary<int, string> { [keep.Id] = keep.Url, [remove.Id] = remove.Url }));
        var saved = await storage.QueryAsync(db => db.Groups.Include(group => group.Category).SingleAsync(group => group.Id == keep.Id));
        Assert.Equal("Local name", saved.Name);
        Assert.Equal("Favorites", saved.Category?.Name);
        Assert.Equal(2400, saved.MemberCount);
        Assert.Equal("Private", saved.FacebookVisibility);
        Assert.Equal("10 posts a day", saved.FacebookActivity);
        var history = await storage.QueryAsync(db => db.Attempts.Include(attempt => attempt.Target).SingleAsync());
        Assert.Null(history.Target.SocialGroupId);
        Assert.Equal("Remove", history.Target.GroupName);
    }

    [Fact]
    public async Task FacebookPostImportDeduplicatesTrackingLinksAndPreservesDrafts()
    {
        var storage = Open();
        await storage.InitializeAsync();
        await storage.SavePostAsync(new Post
        {
            Kind = PostKind.Existing, Title = "My draft", Url = "https://m.facebook.com/example/posts/1/?ref=feed"
        });
        var batch = new Post[]
        {
            new() { Title = "Duplicate", Url = "https://www.facebook.com/example/posts/1/" },
            new() { Title = "New", Body = "Caption", Url = "https://www.facebook.com/story.php?ref=feed&story_fbid=2&id=3" },
            new() { Title = "Comment", Url = "https://www.facebook.com/example/posts/4/?comment_id=5" },
            new() { Title = "Feed", Url = "https://www.facebook.com/example/posts/" }
        };

        Assert.Equal(1, await storage.ImportFacebookPostsAsync(batch));
        Assert.Equal(0, await storage.ImportFacebookPostsAsync(batch));
        var saved = await storage.QueryAsync(db => db.Posts.OrderBy(post => post.Id).ToListAsync());
        Assert.Equal("My draft", saved[0].Title);
        Assert.Equal("https://www.facebook.com/story.php?id=3&story_fbid=2", saved[1].Url);
        Assert.Equal(PostKind.Existing, saved[1].Kind);
        Assert.Equal("Caption", saved[1].Body);
    }

    [Fact]
    public async Task DeletingLibraryPostsKeepsCampaignReferencesAndRemovesUnusedDrafts()
    {
        var storage = Open();
        await storage.InitializeAsync();
        var used = new Post { Kind = PostKind.New, Title = "Campaign draft", Body = "Content" };
        var unused = new Post
        {
            Kind = PostKind.New, Title = "Unused draft", Body = "Content",
            Media = [new PostMedia { Path = "preview.jpg" }],
            Variants = [new PostVariant { Body = "Alternate" }]
        };
        await storage.SavePostAsync(used);
        await storage.SavePostAsync(unused);
        var group = new SocialGroup { Name = "Group", Url = "https://facebook.com/groups/example/" };
        await storage.SaveGroupAsync(group, "");
        await storage.SaveCampaignAsync("Campaign", used.Id, [group.Id], null);

        await storage.DeletePostFromLibraryAsync(unused.Id);
        await storage.DeletePostFromLibraryAsync(used.Id);
        await storage.DeletePostFromLibraryAsync(unused.Id);
        var remaining = await storage.QueryAsync(db => db.Posts.SingleAsync());
        Assert.Equal(used.Id, remaining.Id);
        Assert.True(remaining.IsArchived);
        Assert.Empty(await storage.QueryAsync(db => db.Media.ToListAsync()));
        Assert.Empty(await storage.QueryAsync(db => db.Variants.ToListAsync()));
        Assert.Equal(used.Id, await storage.QueryAsync(db => db.Campaigns.Select(item => item.PostId).SingleAsync()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveCampaignAsync("Another", used.Id, [group.Id], null));
    }

    [Fact]
    public async Task ReimportRefreshesThumbnailWithoutChangingSavedText()
    {
        var storage = Open();
        await storage.InitializeAsync();
        var original = new Post
        {
            Kind = PostKind.Existing, Title = "Edited", Body = "My text",
            Url = "https://www.facebook.com/example/posts/42"
        };
        await storage.SavePostAsync(original);
        var preview = new Post
        {
            Url = original.Url, Title = "Facebook text", Body = "Imported body",
            ThumbnailUrl = "https://scontent.example.fbcdn.net/photo.jpg"
        };
        Assert.Equal(0, await storage.ImportFacebookPostsAsync([preview]));
        var saved = await storage.QueryAsync(db => db.Posts.SingleAsync());
        Assert.Equal("My text", saved.Body);
        Assert.Equal("Edited", saved.Title);
        Assert.Equal(preview.ThumbnailUrl, saved.ThumbnailUrl);
    }

    [Fact]
    public async Task RemovingCampaignPreservesHistoryAndPreventsFurtherRuns()
    {
        var storage = Open();
        await storage.InitializeAsync();
        var group = new SocialGroup { Name = "Group", Url = "https://facebook.com/groups/example/" };
        await storage.SaveGroupAsync(group, "");
        var post = new Post
        {
            Kind = PostKind.Existing, Title = "Post", Url = "https://facebook.com/example/posts/42/"
        };
        await storage.SavePostAsync(post);
        var campaignId = await storage.SaveCampaignAsync("Campaign", post.Id, [group.Id], null);
        var target = await storage.QueryAsync(db => db.Targets.SingleAsync());
        await storage.RecordAttemptAsync(target.Id, TargetStatus.Failed, "");

        await storage.ArchiveCampaignAsync(campaignId);
        await storage.ArchiveCampaignAsync(campaignId);
        Assert.True(await storage.QueryAsync(db => db.Campaigns.Select(campaign => campaign.IsArchived).SingleAsync()));
        Assert.Equal(target.Id, await storage.QueryAsync(db => db.Attempts.Select(attempt => attempt.CampaignGroupId).SingleAsync()));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.BeginTargetAsync(target.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.RecordAttemptAsync(target.Id, TargetStatus.Skipped, ""));
    }

    [Fact]
    public async Task GroupImportAndEditKeepValidatedThumbnail()
    {
        var storage = Open();
        await storage.InitializeAsync();
        var group = new SocialGroup
        {
            Name = "Imported", Url = "https://facebook.com/groups/example/",
            FacebookImageUrl = "https://scontent.example.fbcdn.net/group.jpg"
        };
        Assert.Equal((1, 0), await storage.ImportGroupsAsync([group]));
        var saved = await storage.QueryAsync(db => db.Groups.SingleAsync());
        Assert.Equal(group.FacebookImageUrl, saved.FacebookImageUrl);

        await storage.SaveGroupAsync(new SocialGroup { Id = saved.Id, Url = saved.Url, Name = "Local name" }, "");
        Assert.Equal(group.FacebookImageUrl, await storage.QueryAsync(db => db.Groups.Select(item => item.FacebookImageUrl).SingleAsync()));
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private sealed class Factory(DbContextOptions<PilotDb> options) : IDbContextFactory<PilotDb>
    {
        public PilotDb CreateDbContext() => new(options);
    }
}

using GroupPilot.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace GroupPilot.Infrastructure;

public sealed class Storage(IDbContextFactory<PilotDb> factory, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task InitializeAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async Task<T> QueryAsync<T>(Func<PilotDb, Task<T>> query)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await query(db);
    }

    public async Task SaveAsync(Func<PilotDb, Task> update)
    {
        await using var db = await factory.CreateDbContextAsync();
        await update(db);
        await db.SaveChangesAsync();
    }

    public async Task<int> ImportFacebookPostsAsync(IReadOnlyCollection<Post> posts)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var existing = await db.Posts.Where(post => post.Kind == PostKind.Existing && !post.IsArchived).ToListAsync();
        var known = existing.Select(post => (Url: Validation.FacebookPostImportUrl(post.Url), Post: post))
            .Where(entry => entry.Url is not null).GroupBy(entry => entry.Url!)
            .ToDictionary(group => group.Key, group => group.First().Post);
        var added = 0;
        foreach (var post in posts.Reverse())
        {
            var url = Validation.FacebookPostImportUrl(post.Url);
            if (url is null)
                continue;

            var image = Validation.IsFacebookThumbnailUrl(post.ThumbnailUrl) ? post.ThumbnailUrl : "";
            if (known.TryGetValue(url, out var saved))
            {
                if (image.Length > 0)
                    saved.ThumbnailUrl = image;
                if (string.IsNullOrWhiteSpace(saved.Body) && !string.IsNullOrWhiteSpace(post.Body))
                    saved.Body = post.Body;
                continue;
            }

            var draft = new Post
            {
                Kind = PostKind.Existing, Url = url, Title = post.Title, Body = post.Body, ThumbnailUrl = image
            };
            Validation.CheckPost(draft);
            db.Posts.Add(draft);
            known.Add(url, draft);
            added++;
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return added;
    }

    public async Task DeletePostFromLibraryAsync(int postId)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var post = await db.Posts.Include(item => item.Media).Include(item => item.Variants)
            .SingleOrDefaultAsync(item => item.Id == postId);
        if (post is null)
            return;

        if (await db.Campaigns.AnyAsync(campaign => campaign.PostId == postId))
            post.IsArchived = true;
        else
        {
            db.Media.RemoveRange(post.Media);
            db.Variants.RemoveRange(post.Variants);
            db.Posts.Remove(post);
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public Task SavePostAsync(Post post)
    {
        Validation.CheckPost(post);
        return SaveAsync(async db =>
        {
            if (post.Id == 0)
            {
                db.Posts.Add(post);
                return;
            }

            var existing = await db.Posts.Include(item => item.Media).Include(item => item.Variants)
                .SingleAsync(item => item.Id == post.Id);
            post.IsArchived = existing.IsArchived;
            db.Entry(existing).CurrentValues.SetValues(post);
            db.Media.RemoveRange(existing.Media);
            db.Variants.RemoveRange(existing.Variants);
            existing.Media = post.Media;
            existing.Variants = post.Variants;
        });
    }

    public Task SaveGroupAsync(SocialGroup group, string category)
    {
        group.Url = Validation.GroupUrl(group.Url);
        if (string.IsNullOrWhiteSpace(group.Name))
            throw new ArgumentException("Enter a group name.");
        return SaveAsync(async db =>
        {
            if (await db.Groups.AnyAsync(item => item.Url == group.Url && item.Id != group.Id))
                throw new ArgumentException("This group already exists.");

            var existing = group.Id == 0 ? db.Groups.Add(group).Entity : await db.Groups.FindAsync(group.Id)
                ?? throw new InvalidOperationException("The group was deleted.");
            var sameUrl = existing.Url == group.Url;
            group.PostingAccess = sameUrl ? existing.PostingAccess : GroupPostingAccess.Unreviewed;
            group.ReviewedAt = sameUrl ? existing.ReviewedAt : null;
            if (sameUrl)
                CopyFacebookMetadata(existing, group);
            db.Entry(existing).CurrentValues.SetValues(group);
            existing.Category = category.Length == 0 ? null :
                await db.Categories.SingleOrDefaultAsync(item => item.Name == category)
                ?? new GroupCategory { Name = category };
        });
    }

    public Task<(int Added, int Skipped)> ImportGroupsAsync(string path)
    {
        using var csv = new TextFieldParser(path);
        csv.SetDelimiters(",");
        var rows = new Dictionary<string, string>();
        var skipped = 0;
        var firstRow = true;
        while (!csv.EndOfData)
        {
            var fields = csv.ReadFields() ?? [];
            var urlField = Array.FindIndex(fields, value => Validation.IsFacebookUrl(value));
            if (firstRow && urlField < 0 && fields.Any(value => value.Equals("url", StringComparison.OrdinalIgnoreCase)))
            {
                firstRow = false;
                continue;
            }
            firstRow = false;
            try
            {
                var url = Validation.GroupUrl(urlField < 0 ? "" : fields[urlField]);
                var name = fields.FirstOrDefault(value => value.Length > 0 && !Validation.IsWebUrl(value));
                if (!rows.TryAdd(url, name ?? new Uri(url).Segments[^1].TrimEnd('/')))
                    skipped++;
            }
            catch (ArgumentException)
            {
                skipped++;
            }
        }

        return ImportGroupsAsync(rows.Select(row => new SocialGroup { Url = row.Key, Name = row.Value }), skipped);
    }

    public Task<(int Added, int Skipped)> ImportGroupsAsync(IEnumerable<SocialGroup> groups) =>
        ImportGroupsAsync(groups, 0);

    public async Task<(int Added, int Updated, int Removed)> SyncFacebookGroupsAsync(
        IReadOnlyCollection<SocialGroup> scanned, IReadOnlyDictionary<int, string> expectedDirectory)
    {
        if (scanned.Count == 0)
            throw new ArgumentException("Load at least one Facebook group before syncing.");

        var incoming = scanned.ToDictionary(group => Validation.GroupUrl(group.Url));
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var existing = await db.Groups.ToListAsync();
        if (existing.Count != expectedDirectory.Count ||
            existing.Any(group => !expectedDirectory.TryGetValue(group.Id, out var url) || url != group.Url))
            throw new ArgumentException("The group directory changed. Preview the sync again.");

        var removed = 0;
        var updated = 0;
        foreach (var saved in existing)
        {
            if (incoming.Remove(saved.Url, out var match))
            {
                CopyFacebookMetadata(match, saved);
                updated++;
            }
            else
            {
                db.Groups.Remove(saved);
                removed++;
            }
        }

        foreach (var (url, group) in incoming)
            db.Groups.Add(NewImportedGroup(group, url));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return (incoming.Count, updated, removed);
    }

    private static SocialGroup NewImportedGroup(SocialGroup source, string url)
    {
        var group = new SocialGroup
        {
            Url = url, Name = string.IsNullOrWhiteSpace(source.Name)
                ? new Uri(url).Segments[^1].TrimEnd('/') : source.Name.Trim()
        };
        CopyFacebookMetadata(source, group);
        return group;
    }

    private static void CopyFacebookMetadata(SocialGroup source, SocialGroup target)
    {
        target.MemberCount = source.MemberCount;
        target.FacebookVisibility = source.FacebookVisibility;
        target.FacebookActivity = source.FacebookActivity;
        target.FacebookCardText = source.FacebookCardText;
        if (Validation.IsFacebookThumbnailUrl(source.FacebookImageUrl))
            target.FacebookImageUrl = source.FacebookImageUrl;
        target.FacebookScannedAt = source.FacebookScannedAt;
    }

    private async Task<(int Added, int Skipped)> ImportGroupsAsync(IEnumerable<SocialGroup> groups, int skipped)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var added = 0;
        var existing = (await db.Groups.Select(group => group.Url).ToListAsync()).ToHashSet();
        foreach (var group in groups)
        {
            string url;
            try
            {
                url = Validation.GroupUrl(group.Url);
            }
            catch (ArgumentException)
            {
                skipped++;
                continue;
            }

            if (!existing.Add(url))
            {
                skipped++;
                continue;
            }

            db.Groups.Add(NewImportedGroup(group, url));
            added++;
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return (added, skipped);
    }

    public async Task<int> SaveCampaignAsync(string name, int postId, IReadOnlyCollection<int> groupIds,
        DateTimeOffset? schedule, int pauseSeconds = 120, CampaignRecurrence recurrence = CampaignRecurrence.Once,
        string? timeZoneId = null, int? campaignId = null, int? expectedRunNumber = null)
    {
        var ids = groupIds.Distinct().ToArray();
        if (string.IsNullOrWhiteSpace(name) || ids.Length == 0)
            throw new ArgumentException("Enter a campaign name and select at least one group.");
        CheckPause(pauseSeconds);
        if (!Enum.IsDefined(recurrence))
            throw new ArgumentException("Choose a campaign recurrence.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId ?? TimeZoneInfo.Local.Id);

        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var campaign = campaignId is { } id
            ? await db.Campaigns.Include(item => item.Targets).SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived)
                ?? throw new ArgumentException("Campaign unavailable.")
            : new Campaign();
        if (expectedRunNumber is { } expected && campaign.RunNumber != expected)
            throw new ArgumentException("This campaign changed. Reload it before saving.");
        var groups = await db.Groups.Where(group => ids.Contains(group.Id)).ToListAsync();
        var current = campaign.Targets.Where(target => !target.IsExcluded).ToArray();
        if (groups.Count != ids.Length || groups.Any(group => !group.Enabled && !current.Any(target => target.SocialGroupId == group.Id)))
            throw new ArgumentException("One of the selected groups is unavailable.");
        var post = await db.Posts.FindAsync(postId);
        if (post is null || (post.IsArchived && (campaignId is null || campaign.PostId != postId)))
            throw new InvalidOperationException("The selected post is unavailable.");

        var postChanged = campaignId is not null && campaign.PostId != postId;
        var currentIds = current.Select(target => target.Id).ToArray();
        var hasHistory = campaignId is not null && (current.Any(target => target.Status != TargetStatus.Pending)
            || await db.Attempts.AnyAsync(attempt => currentIds.Contains(attempt.CampaignGroupId)));
        if (postChanged && hasHistory)
        {
            foreach (var target in current)
                target.IsExcluded = true;
            campaign.RunNumber++;
            current = [];
        }
        var remaining = groups.ToDictionary(group => group.Id);
        foreach (var target in current)
        {
            if (target.SocialGroupId is { } groupId && remaining.TryGetValue(groupId, out var group) && group.Url == target.GroupUrl)
                remaining.Remove(groupId);
            else
                target.IsExcluded = true;
        }
        foreach (var group in remaining.Values)
            campaign.Targets.Add(new CampaignGroup { SocialGroupId = group.Id, GroupName = group.Name, GroupUrl = group.Url });

        var scheduleChanged = campaignId is null || postChanged || campaign.ScheduledFor != schedule
            || campaign.Recurrence != recurrence || campaign.TimeZoneId != zone.Id;
        campaign.Name = name.Trim();
        campaign.PostId = postId;
        campaign.PauseSeconds = pauseSeconds;
        campaign.Recurrence = recurrence;
        campaign.TimeZoneId = zone.Id;
        campaign.ScheduledFor = schedule;
        if (scheduleChanged)
            campaign.RecurrenceAnchorAt = schedule;

        var active = campaign.Targets.Where(target => !target.IsExcluded).ToArray();
        var complete = active.All(target => target.Status is TargetStatus.Published or TargetStatus.Skipped);
        if (complete && scheduleChanged)
        {
            if (recurrence != CampaignRecurrence.Once)
                campaign.RecurrenceAnchorAt ??= clock.GetUtcNow();
            campaign.NextRunAt = CampaignSchedule.NextAfter(campaign, clock.GetUtcNow());
        }
        else if (!complete)
        {
            campaign.NextRunAt = active.Any(target => target.Status != TargetStatus.Pending) ? null : schedule;
        }
        if (campaignId is null)
            db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return campaign.Id;
    }

    public async Task ArchiveCampaignAsync(int campaignId)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Campaigns.Where(campaign => campaign.Id == campaignId)
            .ExecuteUpdateAsync(update => update.SetProperty(campaign => campaign.IsArchived, true));
    }

    public Task SaveGroupReviewAsync(int groupId, string reviewedUrl, GroupPostingAccess access, string rules)
    {
        if (access is not (GroupPostingAccess.Allowed or GroupPostingAccess.RequiresApproval or GroupPostingAccess.Unavailable))
            throw new ArgumentException("Choose the posting access you observed.");

        return SaveAsync(async db =>
        {
            var reviewedAt = DateTimeOffset.UtcNow;
            var updated = await db.Groups.Where(group => group.Id == groupId && group.Url == reviewedUrl)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(group => group.PostingAccess, access)
                    .SetProperty(group => group.ReviewedAt, reviewedAt)
                    .SetProperty(group => group.Rules, rules.Trim()));
            if (updated != 1)
                throw new ArgumentException("The group changed. Open its current link and review it again.");
        });
    }

    public Task<DateTimeOffset?> GetNextShareAtAsync(string? nextGroupUrl = null) =>
        QueryAsync(db => db.Settings.Where(settings => settings.Id == 1 &&
                (nextGroupUrl == null || settings.LastShareGroupUrl != nextGroupUrl))
            .Select(settings => settings.NextShareAt).FirstOrDefaultAsync());

    public async Task PrepareCampaignRunAsync(int campaignId, bool runNow = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var campaign = await db.Campaigns.Include(item => item.Targets)
            .SingleOrDefaultAsync(item => item.Id == campaignId && !item.IsArchived)
            ?? throw new ArgumentException("Campaign unavailable.");
        var targets = campaign.Targets.Where(target => !target.IsExcluded).ToArray();
        var complete = targets.Length > 0 &&
            targets.All(target => target.Status is TargetStatus.Published or TargetStatus.Skipped);
        if (complete && (campaign.Recurrence != CampaignRecurrence.Once || runNow))
        {
            if (!runNow && (campaign.NextRunAt is null || campaign.NextRunAt > clock.GetUtcNow()))
                throw new ArgumentException($"The next run is scheduled for {campaign.NextRunAt?.ToLocalTime():g}.");
            foreach (var target in targets)
                target.Status = TargetStatus.Pending;
            campaign.RunNumber++;
            campaign.NextRunAt = null;
        }
        else if (!complete)
        {
            campaign.NextRunAt = null;
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static void CheckPause(int seconds)
    {
        if (seconds < 1 || seconds > 86400)
            throw new ArgumentException("Choose a pause between 1 and 86400 seconds.");
    }

    public Task UpdateCampaignPauseAsync(int campaignId, int seconds)
    {
        CheckPause(seconds);
        return SaveAsync(async db =>
        {
            var campaign = await db.Campaigns.SingleOrDefaultAsync(item => item.Id == campaignId && !item.IsArchived)
                ?? throw new ArgumentException("Campaign unavailable.");
            campaign.PauseSeconds = seconds;
        });
    }

    public async Task BeginTargetAsync(int targetId, int? expectedRunNumber = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var campaign = await db.Campaigns.SingleOrDefaultAsync(item =>
            !item.IsArchived && item.Targets.Any(target => target.Id == targetId && !target.IsExcluded))
            ?? throw new ArgumentException("This campaign is no longer available.");
        if (expectedRunNumber is { } expected && campaign.RunNumber != expected)
            throw new ArgumentException("This campaign has started a new run. Reload before preparing or recording a share.");
        var target = await db.Targets.FindAsync(targetId) ?? throw new ArgumentException("Target unavailable.");
        if (target.Status is TargetStatus.Published or TargetStatus.Skipped or TargetStatus.AwaitingApproval)
            throw new ArgumentException("This target is closed or awaiting approval.");
        var group = await db.Groups.FindAsync(target.SocialGroupId);
        if (group is null || !group.Enabled || group.Url != target.GroupUrl || group.PostingAccess == GroupPostingAccess.Unavailable)
            throw new ArgumentException("This group is unavailable or its saved URL changed.");

        var settings = await db.Settings.FindAsync(1);
        var now = clock.GetUtcNow();
        if (settings?.NextShareAt is { } next && next > now && settings.LastShareGroupUrl != target.GroupUrl)
            throw new ArgumentException($"Wait {Math.Ceiling((next - now).TotalSeconds)} seconds before opening a different group.");
        if (campaign.Recurrence != CampaignRecurrence.Once)
            campaign.RecurrenceAnchorAt ??= campaign.ScheduledFor ?? now;
        campaign.NextRunAt = null;
        target.Status = TargetStatus.InProgress;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task RecordAttemptAsync(int targetId, TargetStatus status, string publishedUrl, int? expectedRunNumber = null)
    {
        if (status is not (TargetStatus.Published or TargetStatus.Failed or TargetStatus.Skipped or TargetStatus.AwaitingApproval)
            || (status == TargetStatus.Published && !Validation.IsFacebookPostUrl(publishedUrl)))
            throw new ArgumentException("Choose an outcome and enter the Facebook post URL for a successful publication.");

        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var campaign = await db.Campaigns.SingleOrDefaultAsync(item =>
            !item.IsArchived && item.Targets.Any(target => target.Id == targetId && !target.IsExcluded))
            ?? throw new ArgumentException("This campaign is no longer available.");
        if (expectedRunNumber is { } expected && campaign.RunNumber != expected)
            throw new ArgumentException("This campaign has started a new run. Reload before preparing or recording a share.");
        var target = await db.Targets.FindAsync(targetId) ?? throw new InvalidOperationException("Target unavailable.");
        if (target.Status is TargetStatus.Published or TargetStatus.Skipped)
            throw new InvalidOperationException("This target is already closed.");

        var now = clock.GetUtcNow();
        if (status is TargetStatus.Published or TargetStatus.AwaitingApproval or TargetStatus.Failed)
        {
            var settings = await db.Settings.FindAsync(1);
            if (settings is null)
            {
                settings = new AppSettings { Id = 1 };
                db.Settings.Add(settings);
            }
            var next = now.AddSeconds(campaign.PauseSeconds);
            if (settings.NextShareAt is null || next > settings.NextShareAt)
                settings.NextShareAt = next;
            settings.LastShareGroupUrl = target.GroupUrl;
        }
        target.Status = status;
        db.Attempts.Add(new PublishingAttempt
        {
            CampaignGroupId = targetId, Status = status, RecordedAt = now, RunNumber = campaign.RunNumber,
            PublishedUrl = status == TargetStatus.Published ? publishedUrl : ""
        });
        await db.Entry(campaign).Collection(item => item.Targets).LoadAsync();
        if (campaign.Targets.Where(item => !item.IsExcluded).All(item => item.Status is TargetStatus.Published or TargetStatus.Skipped))
        {
            if (campaign.Recurrence != CampaignRecurrence.Once)
                campaign.RecurrenceAnchorAt ??= campaign.ScheduledFor ?? now;
            campaign.NextRunAt = CampaignSchedule.NextAfter(campaign, now);
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}

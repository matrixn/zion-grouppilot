using GroupPilot.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace GroupPilot.Infrastructure;

public sealed class Storage(IDbContextFactory<PilotDb> factory)
{
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

            db.Groups.Add(new SocialGroup
            {
                Url = url, Name = string.IsNullOrWhiteSpace(group.Name)
                    ? new Uri(url).Segments[^1].TrimEnd('/') : group.Name.Trim()
            });
            added++;
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return (added, skipped);
    }

    public Task SaveCampaignAsync(string name, int postId, IReadOnlyCollection<int> groupIds, DateTimeOffset? schedule)
    {
        if (string.IsNullOrWhiteSpace(name) || groupIds.Count == 0)
            throw new ArgumentException("Enter a campaign name and select at least one group.");

        return SaveAsync(async db =>
        {
            var groups = await db.Groups.Where(group => group.Enabled && groupIds.Contains(group.Id)).ToListAsync();
            if (groups.Count != groupIds.Count || !await db.Posts.AnyAsync(post => post.Id == postId))
                throw new InvalidOperationException("The selected post or groups have changed. Reload and try again.");

            db.Campaigns.Add(new Campaign
            {
                Name = name, PostId = postId, ScheduledFor = schedule,
                Targets = groups.Select(group => new CampaignGroup
                {
                    SocialGroupId = group.Id, GroupName = group.Name, GroupUrl = group.Url
                }).ToList()
            });
        });
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

    public Task BeginTargetAsync(int targetId) => SaveAsync(async db =>
    {
        var target = await db.Targets.FindAsync(targetId) ?? throw new ArgumentException("Target unavailable.");
        if (target.Status is TargetStatus.Published or TargetStatus.Skipped or TargetStatus.AwaitingApproval)
            throw new ArgumentException("This target is closed or awaiting approval.");
        var group = await db.Groups.FindAsync(target.SocialGroupId);
        if (group is null || !group.Enabled || group.Url != target.GroupUrl || group.PostingAccess == GroupPostingAccess.Unavailable)
            throw new ArgumentException("This group is unavailable or its saved URL changed.");

        target.Status = TargetStatus.InProgress;
    });

    public Task RecordAttemptAsync(int targetId, TargetStatus status, string publishedUrl)
    {
        if (status is not (TargetStatus.Published or TargetStatus.Failed or TargetStatus.Skipped or TargetStatus.AwaitingApproval) || (status == TargetStatus.Published && !Validation.IsFacebookPostUrl(publishedUrl)))
            throw new ArgumentException("Choose an outcome and enter the Facebook post URL for a successful publication.");

        return SaveAsync(async db =>
        {
            var target = await db.Targets.FindAsync(targetId) ?? throw new InvalidOperationException("Target unavailable.");
            if (target.Status is TargetStatus.Published or TargetStatus.Skipped)
                throw new InvalidOperationException("This target is already closed.");

            target.Status = status;
            db.Attempts.Add(new PublishingAttempt
            {
                CampaignGroupId = targetId, Status = status, RecordedAt = DateTimeOffset.UtcNow,
                PublishedUrl = status == TargetStatus.Published ? publishedUrl : ""
            });
        });
    }
}

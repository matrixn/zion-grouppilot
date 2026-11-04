namespace GroupPilot.Core;

public enum PostKind { Existing, New, Remix }
public enum TargetStatus { Pending, Published, Failed, Skipped, InProgress, AwaitingApproval, NeedsReview }
public enum CampaignRecurrence { Once, Daily, EveryTwoDays, EveryThreeDays, Weekly, Monthly, TwiceMonthly, ThreeTimesMonthly, FourTimesMonthly }
public enum GroupPostingAccess { Unreviewed, Allowed, RequiresApproval, Unavailable }

public sealed class SocialAccount
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class GroupCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class SocialGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public int? CategoryId { get; set; }
    public GroupCategory? Category { get; set; }
    public string Notes { get; set; } = "";
    public string Rules { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public GroupPostingAccess PostingAccess { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public long? MemberCount { get; set; }
    public string FacebookVisibility { get; set; } = "";
    public string FacebookActivity { get; set; } = "";
    public string FacebookCardText { get; set; } = "";
    public string FacebookImageUrl { get; set; } = "";
    public DateTimeOffset? FacebookScannedAt { get; set; }

    public override string ToString() =>
        $"{Name}{(Category is null ? "" : $" · {Category.Name}")} · {PostingAccess}" +
        (MemberCount is { } members ? $" · ~{members:N0} members" : "") +
        (FacebookVisibility.Length > 0 ? $" · {FacebookVisibility}" : "");

}

public sealed class Post
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Url { get; set; } = "";
    public PostKind Kind { get; set; }
    public string ThumbnailUrl { get; set; } = "";
    public bool IsArchived { get; set; }
    public List<PostMedia> Media { get; set; } = [];
    public List<PostVariant> Variants { get; set; } = [];
    public override string ToString() => $"{Title} · {Kind}";
}

public sealed class PostMedia
{
    public int Id { get; set; }
    public string Path { get; set; } = "";
}

public sealed class PostVariant
{
    public int Id { get; set; }
    public string Body { get; set; } = "";
}

public sealed class Campaign
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int PostId { get; set; }
    public Post Post { get; set; } = null!;
    public DateTimeOffset? ScheduledFor { get; set; }
    public int PauseSeconds { get; set; } = 120;
    public CampaignRecurrence Recurrence { get; set; }
    public string TimeZoneId { get; set; } = "";
    public DateTimeOffset? NextRunAt { get; set; }
    public int RunNumber { get; set; } = 1;
    public DateTimeOffset? RecurrenceAnchorAt { get; set; }
    public bool IsArchived { get; set; }
    public List<CampaignGroup> Targets { get; set; } = [];
    public override string ToString() => $"{Name} · {Targets.Count} groups" +
        (ScheduledFor is { } scheduled ? $" · {scheduled.ToLocalTime():g}" : "");
}

public sealed class CampaignGroup
{
    public int Id { get; set; }
    public int? SocialGroupId { get; set; }
    public string GroupName { get; set; } = "";
    public string GroupUrl { get; set; } = "";
    public TargetStatus Status { get; set; }
    public bool IsExcluded { get; set; }
    public DateTimeOffset? SubmissionStartedAt { get; set; }
    public string AutomationMessage { get; set; } = "";
    public override string ToString() => $"{GroupName} · {Status}";
}

public sealed class PublishingAttempt
{
    public int Id { get; set; }
    public int CampaignGroupId { get; set; }
    public CampaignGroup Target { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
    public int RunNumber { get; set; } = 1;
    public TargetStatus Status { get; set; }
    public string PublishedUrl { get; set; } = "";
    public string Evidence { get; set; } = "";
    public bool IsAutomatic { get; set; }
    public override string ToString() => $"{RecordedAt.ToLocalTime():g} · Run {RunNumber} · {Target.GroupName} · {Status} {PublishedUrl}" +
        (Evidence.Length == 0 ? "" : $" · {Evidence}");
}

public sealed class AppSettings
{
    public int Id { get; set; }
    public string Theme { get; set; } = "Default";
    public DateTimeOffset? NextShareAt { get; set; }
    public string LastShareGroupUrl { get; set; } = "";
}

using System.Text.RegularExpressions;

namespace GroupPilot.Core;

public sealed class FacebookPageSnapshot
{
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public int ComposerCount { get; set; }
    public int EntryCount { get; set; }
    public int EditorCount { get; set; }
    public int SubmitCount { get; set; }
    public int DialogCount { get; set; }
    public string EditorText { get; set; } = "";
    public bool HasSource { get; set; }
    public string[] Notices { get; set; } = [];
    public FacebookArticle[] Articles { get; set; } = [];
}

public sealed class FacebookArticle
{
    public string Url { get; set; } = "";
    public bool HasSource { get; set; }
    public long? Timestamp { get; set; }
}

public static class FacebookPublicationEvidence
{
    public static PublicationResult? Observe(CampaignPublication publication, FacebookPageSnapshot baseline,
        FacebookPageSnapshot current, DateTimeOffset submittedAt)
    {
        var fresh = current.Notices.Except(baseline.Notices, StringComparer.Ordinal).ToArray();
        bool Matches(string pattern) => fresh.Any(value => Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        if (Matches(@"(?:your post|the post|postarea(?: ta)?).{0,60}(?:submitted for approval|pending approval|awaiting approval|în așteptarea aprobării|trimis[ăa] spre aprobare)|^(?:submitted for approval|post pending approval)"))
            return new(TargetStatus.AwaitingApproval, "Facebook confirmed submission for group approval.");
        if (Matches(@"couldn't (?:share|publish|post)|could not (?:share|publish|post)|something went wrong|nu s-a putut (?:publica|distribui)|a ap[ăa]rut o eroare"))
            return new(TargetStatus.NeedsReview, "Facebook reported an error after the Publish click. Check the group before retrying.");
        var known = baseline.Articles.Select(article => article.Url).ToHashSet(StringComparer.Ordinal);
        var article = current.Articles.FirstOrDefault(article => article.HasSource && !known.Contains(article.Url)
            && Validation.IsFacebookPostUrl(article.Url) && Validation.IsSameFacebookGroup(publication.GroupUrl, article.Url)
            && article.Timestamp is { } time && time >= submittedAt.AddSeconds(-15).ToUnixTimeMilliseconds()
            && time <= DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds());
        if (Matches(@"(?:your post|the post).{0,40}(?:was published|has been published|is now published|was posted|has been posted)|^post (?:published|posted)|postarea (?:ta )?(?:a fost|este) publicat[ăa]|^ai publicat o postare"))
            return new(TargetStatus.Published, "Facebook displayed a new publication confirmation.", article?.Url ?? "");
        if (article is not null && current.ComposerCount == 0)
            return new(TargetStatus.Published, "Verified a new group permalink containing the selected source post and a current timestamp.", article.Url);
        return null;
    }
}

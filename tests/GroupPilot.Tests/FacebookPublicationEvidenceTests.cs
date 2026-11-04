using GroupPilot.Core;

namespace GroupPilot.Tests;

public sealed class FacebookPublicationEvidenceTests
{
    private static readonly CampaignPublication Publication = new(1, 1, 1, "https://www.facebook.com/groups/example/", "Example", "https://www.facebook.com/source/posts/123/");
    private static readonly DateTimeOffset SubmittedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
    private static FacebookArticle Article(string url = "https://www.facebook.com/groups/example/posts/456/", bool source = true, long? time = null) =>
        new() { Url = url, HasSource = source, Timestamp = time ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };

    [Fact]
    public void OldToastAndDisappearedComposerDoNotProvePublication()
    {
        var old = new FacebookPageSnapshot { Notices = ["Your post was published."] };
        Assert.Null(FacebookPublicationEvidence.Observe(Publication, old, new() { Notices = old.Notices }, SubmittedAt));
        Assert.Null(FacebookPublicationEvidence.Observe(Publication, new(), new(), SubmittedAt));
    }

    [Fact]
    public void NewApprovalNoticeRemainsSeparateFromPublished()
    {
        var outcome = FacebookPublicationEvidence.Observe(Publication, new(), new() { Notices = ["Your post is pending approval."] }, SubmittedAt);
        Assert.Equal(TargetStatus.AwaitingApproval, outcome?.Status);
    }

    [Fact]
    public void ExplicitErrorAfterClickRequiresReview()
    {
        var outcome = FacebookPublicationEvidence.Observe(Publication, new(), new() { Notices = ["Couldn't publish your post."] }, SubmittedAt);
        Assert.Equal(TargetStatus.NeedsReview, outcome?.Status);
    }

    [Fact]
    public void NewSourceMatchedPermalinkConfirmsPublicationWithoutToast()
    {
        var article = Article();
        var outcome = FacebookPublicationEvidence.Observe(Publication, new(), new() { Articles = [article] }, SubmittedAt);
        Assert.Equal(TargetStatus.Published, outcome?.Status);
        Assert.Equal(article.Url, outcome?.PublishedUrl);
    }

    [Fact]
    public void OldPermalinkIsNotReusedAsConfirmation()
    {
        var article = Article();
        Assert.Null(FacebookPublicationEvidence.Observe(Publication, new() { Articles = [article] }, new() { Articles = [article] }, SubmittedAt));
    }

    [Theory]
    [InlineData("https://www.facebook.com/groups/other/posts/456/", true, 0)]
    [InlineData("https://www.facebook.com/groups/example/posts/456/", false, 0)]
    [InlineData("https://www.facebook.com/groups/example/posts/456/", true, -3600)]
    [InlineData("https://www.facebook.com/groups/example/posts/456/", true, 3600)]
    public void WrongSourceGroupOrTimestampDoesNotConfirm(string url, bool source, int offsetSeconds)
    {
        Assert.Null(FacebookPublicationEvidence.Observe(Publication, new(), new()
        {
            Articles = [Article(url, source, DateTimeOffset.UtcNow.AddSeconds(offsetSeconds).ToUnixTimeMilliseconds())]
        }, SubmittedAt));
    }
}

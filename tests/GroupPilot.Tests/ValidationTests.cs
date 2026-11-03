using GroupPilot.Core;

namespace GroupPilot.Tests;

public sealed class ValidationTests
{
    [Theory]
    [InlineData("https://www.facebook.com.evil.test/groups/example/")]
    [InlineData("https://facebook.com@evil.test/groups/example/")]
    [InlineData("https://www.facebook.com/groups/discover/")]
    [InlineData("http://www.facebook.com/groups/example/")]
    public void RejectsInvalidGroupUrls(string url) => Assert.Throws<ArgumentException>(() => Validation.GroupUrl(url));

    [Fact]
    public void CanonicalizesPermalinksToGroup()
    {
        Assert.Equal("https://www.facebook.com/groups/example/",
            Validation.GroupUrl("https://m.facebook.com/groups/Example/posts/123/?ref=share"));
    }

    [Theory]
    [InlineData("https://m.facebook.com/groups/Test/posts/42/", true)]
    [InlineData("https://www.facebook.com/groups/testing/", false)]
    [InlineData("https://www.facebook.com/login/?next=https://www.facebook.com/groups/test/", false)]
    public void GroupReviewUsesCurrentGroup(string url, bool same) =>
        Assert.Equal(same, Validation.IsSameFacebookGroup("https://www.facebook.com/groups/test/", url));

    [Theory]
    [InlineData("https://www.facebook.com/login.php", true)]
    [InlineData("https://www.facebook.com/checkpoint/", true)]
    [InlineData("https://www.facebook.com/groups/test/", false)]
    public void DetectsSignInRoutes(string url, bool login) =>
        Assert.Equal(login, Validation.IsFacebookLoginPage(url));

    [Fact]
    public void RequiresDraftContentAndFacebookSource()
    {
        Assert.Throws<ArgumentException>(() => Validation.CheckPost(new Post { Title = "Empty", Kind = PostKind.New }));
        Assert.Throws<ArgumentException>(() => Validation.CheckPost(new Post
            { Title = "Existing", Kind = PostKind.Existing, Url = "https://example.org/" }));
        Validation.CheckPost(new Post { Title = "Draft", Kind = PostKind.New, Body = "Hello" });
    }
}

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

    [Fact]
    public void RequiresDraftContentAndFacebookSource()
    {
        Assert.Throws<ArgumentException>(() => Validation.CheckPost(new Post { Title = "Empty", Kind = PostKind.New }));
        Assert.Throws<ArgumentException>(() => Validation.CheckPost(new Post
            { Title = "Existing", Kind = PostKind.Existing, Url = "https://example.org/" }));
        Validation.CheckPost(new Post { Title = "Draft", Kind = PostKind.New, Body = "Hello" });
    }
}

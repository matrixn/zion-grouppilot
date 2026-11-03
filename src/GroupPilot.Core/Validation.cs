namespace GroupPilot.Core;

public static class Validation
{
    public static bool IsWebUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0;

    public static bool IsFacebookUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.IsDefaultPort
        && (uri.IdnHost.Equals("facebook.com", StringComparison.OrdinalIgnoreCase)
            || uri.IdnHost.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase));

    public static bool IsFacebookPostUrl(string value)
    {
        if (!IsFacebookUrl(value))
            return false;

        var uri = new Uri(value);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment is "posts" or "videos" or "reel" or "permalink" or "share" or
            "story.php" or "permalink.php" or "photo" or "photo.php" or "watch");
    }

    public static string GroupUrl(string value)
    {
        if (!IsFacebookUrl(value))
            throw new ArgumentException("Enter a Facebook group HTTPS URL.");

        var segments = new Uri(value).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || segments[0] != "groups" || segments[1] is "feed" or "discover" or "create" or "joins"
            || !segments[1].All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
            || segments[1] is "." or "..")
            throw new ArgumentException("Enter a Facebook URL containing /groups/<group>.");

        return $"https://www.facebook.com/groups/{segments[1].ToLowerInvariant()}/";
    }

    public static void CheckPost(Post post)
    {
        if (string.IsNullOrWhiteSpace(post.Title))
            throw new ArgumentException("Enter a title.");
        if (post.Kind == PostKind.Existing && !IsFacebookPostUrl(post.Url))
            throw new ArgumentException("Enter an HTTPS Facebook post URL.");
        if (post.Kind != PostKind.Existing && post.Url.Length > 0 && !IsWebUrl(post.Url))
            throw new ArgumentException("Enter a valid HTTP or HTTPS link.");
        if (post.Kind != PostKind.Existing && string.IsNullOrWhiteSpace(post.Body)
            && post.Url.Length == 0 && post.Media.Count == 0)
            throw new ArgumentException("Add text, a link or an image.");
    }
}

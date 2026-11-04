using System.Text.Json;
using GroupPilot.Core;
using Microsoft.UI.Xaml.Controls;

namespace GroupPilot_App;

public sealed partial class MainWindow
{
    private async Task ImportFacebookPostsAsync()
    {
        var source = CurrentFacebookPage();
        var path = new Uri(source).AbsolutePath.Trim('/');
        var section = path.Split('/')[0].ToLowerInvariant();
        if (section is "" or "groups" or "watch" or "reel" or "stories" or "marketplace" or "search" or "share" or "events"
            || Validation.IsFacebookLoginPage(source) || Validation.FacebookPostImportUrl(source) is not null)
            throw new ArgumentException("Open the Facebook profile or Page whose posts you want to import.");

        var core = ReadyBrowser();
        var navigation = navigationId;
        var latest = new Dictionary<string, FacebookPostRow>();
        var scanTime = System.Diagnostics.Stopwatch.StartNew();
        var stablePasses = 0;
        double previousHeight = -1;
        for (var firstPass = true; ; firstPass = false)
        {
            if (browser?.CoreWebView2 != core || navigationId != navigation || CurrentFacebookPage() != source)
                throw new ArgumentException("The page changed. Open the profile or Page and try again.");
            var json = await core.ExecuteScriptAsync("""
                ((expectedUrl, reset) => {
                    if (location.href !== expectedUrl)
                        return null;
                    const main = document.querySelector('main, [role="main"]');
                    const root = main ?? document;
                    const pageName = location.pathname.split('/').filter(Boolean)[0]?.toLowerCase();
                    const cardSelector = 'article, [role="article"], [data-pagelet^="FeedUnit"]';
                    const messageSelector = '[data-ad-preview="message"], [data-ad-comet-preview="message"], [data-ad-rendering-role="story_message"]';
                    const rows = [];
                    const seen = new Set();
                    let firstPost = null;
                    const cards = new Map();
                    for (const link of root.querySelectorAll('a[href]')) {
                        if (!(link instanceof HTMLAnchorElement) || !link.getClientRects().length)
                            continue;
                        const url = new URL(link.href);
                        if (url.protocol !== 'https:' ||
                            !(url.hostname === 'facebook.com' || url.hostname.endsWith('.facebook.com')) ||
                            url.searchParams.has('comment_id') || url.searchParams.has('reply_comment_id') ||
                            link.closest(messageSelector))
                            continue;

                        const parts = url.pathname.split('/').filter(Boolean);
                        const ownedPost = parts.length === 3 && parts[0].toLowerCase() === pageName
                            && ['posts', 'videos'].includes(parts[1]) && /^[a-z0-9_-]+$/i.test(parts[2]);
                        const postLink = ownedPost || (
                            /\/(posts|videos|reel|permalink|share)\/[^/]+/i.test(url.pathname)
                            || (/\/(story\.php|permalink\.php|photo(?:\.php)?|watch)\/?$/i.test(url.pathname)
                                && ['story_fbid', 'fbid', 'v'].some(key => url.searchParams.has(key))));
                        if (!postLink || seen.has(url.href))
                            continue;

                        const card = link.closest(cardSelector);
                        const timestamp = link.matches('[data-ad-rendering-role="timestamp"]')
                            || link.querySelector('time, abbr') || link.closest('time, abbr');
                        if (!ownedPost && !(card && (main || timestamp)) && !(main && timestamp))
                            continue;

                        let message = card?.querySelector(messageSelector);
                        let mediaScope = card;
                        if (!card) {
                            for (let parent = link.parentElement, depth = 0;
                                parent && parent !== main && depth < 7; parent = parent.parentElement, depth++) {
                                const messages = parent.querySelectorAll(messageSelector);
                                if (messages.length > 1)
                                    break;
                                if (messages.length === 1) {
                                    message = messages[0];
                                    mediaScope = parent;
                                    break;
                                }
                            }
                        }
                        firstPost ??= card ?? link;
                        seen.add(url.href);
                        const images = Array.from((mediaScope ?? link).querySelectorAll('img'));
                        const photo = images.filter(img => img.naturalWidth >= 150 && img.naturalHeight >= 100)
                            .sort((a, b) => b.naturalWidth * b.naturalHeight - a.naturalWidth * a.naturalHeight)[0];
                        const image = photo?.currentSrc || photo?.src || mediaScope?.querySelector('video[poster]')?.poster || '';
                        const row = { Url: url.href, Body: (message?.innerText?.trim() ?? '').slice(0, 4000), Image: image };
                        const previous = card && cards.get(card);
                        const rank = timestamp ? 2 : ownedPost ? 1 : 0;
                        if (previous) {
                            if (rank > previous.rank) {
                                rows[previous.index] = row;
                                previous.rank = rank;
                            }
                            continue;
                        }
                        if (card)
                            cards.set(card, { index: rows.length, rank });
                        rows.push(row);
                        if (rows.length === 60)
                            break;
                    }
                    let scroller = firstPost?.parentElement ?? main;
                    while (scroller && !(/auto|scroll|overlay/.test(getComputedStyle(scroller).overflowY)
                        && scroller.scrollHeight > scroller.clientHeight + 2))
                        scroller = scroller.parentElement;
                    scroller ??= document.scrollingElement;
                    const height = scroller?.scrollHeight ?? 0;
                    const atBottom = scroller && height - scroller.clientHeight - scroller.scrollTop < 4;
                    if (scroller)
                        scroller.scrollTo({ top: reset ? 0 : scroller.scrollTop + Math.max(1, scroller.clientHeight * 0.85), behavior: 'instant' });
                    return { Rows: rows, Height: height, AtBottom: !!atBottom };
                })
                """ + $"({JsonSerializer.Serialize(source)}, {(firstPass ? "true" : "false")})");
            if (browser?.CoreWebView2 != core || navigationId != navigation || CurrentFacebookPage() != source)
                throw new ArgumentException("The page changed. Open the profile or Page and try again.");

            var scan = JsonSerializer.Deserialize<FacebookPostScan>(json)
                ?? throw new ArgumentException("The page changed. Open the profile or Page and try again.");
            var previousCount = latest.Count;
            if (!firstPass)
            {
                foreach (var row in scan.Rows)
                {
                    if (Validation.FacebookPostImportUrl(row.Url) is { } url)
                        latest.TryAdd(url, row);
                    if (latest.Count == 10)
                        break;
                }
            }

            stablePasses = !firstPass && scan.AtBottom && scan.Height == previousHeight && latest.Count == previousCount
                ? stablePasses + 1 : 0;
            browserStatus!.Text = $"Scanning Facebook posts… {latest.Count} of 10 found.";
            if (latest.Count == 10 || stablePasses >= 3 || scanTime.Elapsed >= TimeSpan.FromSeconds(25))
                break;

            previousHeight = scan.Height;
            await Task.Delay(1100);
        }

        if (latest.Count == 0)
            throw new ArgumentException("No post links were visible. If Facebook shows a sign-in prompt, sign in here first. Otherwise open the Page's Posts tab and retry.");

        var known = model.Posts.Where(post => post.Kind == PostKind.Existing && !post.IsArchived)
            .Select(post => (Url: Validation.FacebookPostImportUrl(post.Url), post.ThumbnailUrl, post.Body))
            .Where(entry => entry.Url is not null).ToLookup(entry => entry.Url!);
        var posts = latest.Select(row =>
        {
            var image = Validation.IsFacebookThumbnailUrl(row.Value.Image) ? row.Value.Image : "";
            var title = row.Value.Body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return new Post
            {
                Url = row.Key, Kind = PostKind.Existing, Body = row.Value.Body, ThumbnailUrl = image,
                Title = string.IsNullOrWhiteSpace(title) ? "Facebook post" : title[..Math.Min(100, title.Length)]
            };
        }).Where(post => !known.Contains(post.Url)
            || known[post.Url].Any(saved => (post.ThumbnailUrl.Length > 0 && saved.ThumbnailUrl != post.ThumbnailUrl)
                || (post.Body.Length > 0 && string.IsNullOrWhiteSpace(saved.Body)))).ToArray();
        if (posts.Length == 0)
            throw new ArgumentException("The detected posts are already in the Post Library.");

        await PreviewFacebookPostsAsync(posts, $"Found {latest.Count} posts in feed order. Select posts to save or refresh their preview.");
    }

    private async Task PreviewFacebookPostsAsync(Post[] posts, string description)
    {
        var list = List(posts.Select(post => new FacebookPostChoice(post)).ToArray(), ListViewSelectionMode.Multiple);
        list.SelectAll();
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(Label(description));
        content.Children.Add(list);
        var dialog = Dialog("Import Facebook posts", content, "Save selected");
        dialog.PrimaryButtonClick += (_, args) => args.Cancel = list.SelectedItems.Count == 0;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var selected = list.SelectedItems.Cast<FacebookPostChoice>().Select(choice => choice.Post).ToHashSet();
        await storage.ImportFacebookPostsAsync(posts.Where(selected.Contains).ToArray());
        await model.ReloadAsync();
        NavigateTo("Library");
    }

    private sealed record FacebookPostScan(FacebookPostRow[] Rows, double Height, bool AtBottom);

    private sealed record FacebookPostRow(string Url, string Body, string Image);

    private sealed record FacebookPostChoice(Post Post)
    {
        public override string ToString() => $"{Post.Title} | {Post.Url}";
    }
}

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
        var latest = new Dictionary<string, string>();
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
                    if (!main)
                        return { Rows: [], Height: 0, AtBottom: false };
                    const selector = '[role="article"], [data-pagelet^="FeedUnit"]';
                    const cards = Array.from(main.querySelectorAll(selector))
                        .filter(card => !card.parentElement.closest(selector) && card.getClientRects().length);
                    const rows = cards.slice(0, 40).map(card => {
                            const links = Array.from(card.querySelectorAll('a[href]')).filter(link => {
                                if (!(link instanceof HTMLAnchorElement))
                                    return false;
                                const url = new URL(link.href);
                                return (url.hostname === 'facebook.com' || url.hostname.endsWith('.facebook.com'))
                                    && !url.searchParams.has('comment_id') && !url.searchParams.has('reply_comment_id')
                                    && (/\/(posts|videos|reel|permalink|share)\/[^/]+/i.test(url.pathname)
                                        || (/\/(story\.php|permalink\.php|photo(?:\.php)?|watch)\/?$/i.test(url.pathname)
                                            && ['story_fbid', 'fbid', 'v'].some(key => url.searchParams.has(key))));
                            });
                            const permalink = links.find(link => link.querySelector('time, abbr') || link.closest('time, abbr'))
                                ?? links.find(link => !link.closest('[data-ad-preview="message"], [data-ad-comet-preview="message"]'));
                            const body = card.querySelector('[data-ad-preview="message"], [data-ad-comet-preview="message"]')
                                ?.innerText?.trim() ?? '';
                            return { Url: permalink?.href ?? '', Body: body.slice(0, 4000) };
                        });
                    let scroller = cards[0]?.parentElement ?? main;
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
                        latest.TryAdd(url, row.Body);
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
            throw new ArgumentException("No post links found. Open the profile's Posts tab and wait for the feed to load.");

        var known = model.Posts.Where(post => post.Kind == PostKind.Existing).Select(post => Validation.FacebookPostImportUrl(post.Url))
            .OfType<string>().ToHashSet();
        var drafts = latest.Where(row => !known.Contains(row.Key)).Select(row =>
        {
            var title = row.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return new Post
            {
                Url = row.Key, Kind = PostKind.Existing, Body = row.Value,
                Title = string.IsNullOrWhiteSpace(title) ? "Facebook post" : title[..Math.Min(100, title.Length)]
            };
        }).ToArray();
        if (drafts.Length == 0)
            throw new ArgumentException("The detected posts are already in the Post Library.");

        var list = List(drafts.Select(post => new FacebookPostChoice(post)).ToArray(), ListViewSelectionMode.Multiple);
        list.SelectAll();
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(Label($"Found {latest.Count} posts in feed order. Review the links and select what to save."));
        content.Children.Add(list);
        var dialog = Dialog("Import Facebook posts", content, "Save selected");
        dialog.PrimaryButtonClick += (_, args) => args.Cancel = list.SelectedItems.Count == 0;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var selected = list.SelectedItems.Cast<FacebookPostChoice>().Select(choice => choice.Post).ToHashSet();
        await storage.ImportFacebookPostsAsync(drafts.Where(selected.Contains).ToArray());
        await model.ReloadAsync();
        NavigateTo("Library");
    }

    private sealed record FacebookPostScan(FacebookPostRow[] Rows, double Height, bool AtBottom);

    private sealed record FacebookPostRow(string Url, string Body);

    private sealed record FacebookPostChoice(Post Post)
    {
        public override string ToString() => $"{Post.Title} | {Post.Url}";
    }
}

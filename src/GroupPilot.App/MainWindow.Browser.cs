using System.Text.Json;
using GroupPilot.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace GroupPilot_App;

public sealed partial class MainWindow
{
    private const string FacebookGroups = "https://www.facebook.com/groups/joins/";
    private const string FacebookHome = "https://www.facebook.com/";
    private Grid? facebookPage;
    private WebView2? browser;
    private TextBox? browserAddress;
    private TextBlock? browserStatus;
    private TextBlock? browserContext;
    private Button? reviewButton;
    private Button? outcomeButton;
    private Button? importGroupsButton;
    private Button? syncGroupsButton;
    private bool pageLoaded;
    private ulong navigationId;
    private bool browserReady;
    private bool sessionConfirmed;
    private string requestedFacebookUrl = FacebookHome;
    private SocialGroup? reviewGroup;
    private (int CampaignId, int TargetId)? publishingTarget;

    private void ShowBrowser()
    {
        if (facebookPage is null)
            CreateFacebookPage();
        FacebookHost.Content = facebookPage;
        if (!browserReady && !model.Busy)
            _ = model.RunAsync(() => OpenFacebookAsync(FacebookHome, keepCurrentPage: true));
    }

    private async Task OpenFacebookAsync(string url, SocialGroup? group = null,
        (int CampaignId, int TargetId)? target = null, bool keepCurrentPage = false)
    {
        if (!Validation.IsFacebookUrl(url))
            throw new ArgumentException("Enter an HTTPS Facebook URL.");

        if (FacebookHost.Visibility != Visibility.Visible || facebookPage is null)
            NavigateTo("Browser");
        var view = browser!;
        if (!browserReady)
        {
            browserStatus!.Text = "Starting the local Facebook profile…";
            try
            {
                var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                    null, Path.Combine(App.DataDirectory, "browser"), new CoreWebView2EnvironmentOptions());
                if (browser != view)
                    return;
                await view.EnsureCoreWebView2Async(environment);
                if (browser != view)
                    return;
                ConfigureFacebookView(view);
                browserReady = true;
            }
            catch (Exception) when (browser != view)
            {
                return;
            }
            catch
            {
                browserStatus!.Text = "Could not start Facebook. Select Facebook Browser to retry.";
                throw;
            }
        }

        if (keepCurrentPage && Validation.IsFacebookUrl(view.CoreWebView2.Source))
            return;
        requestedFacebookUrl = url;
        reviewGroup = group;
        publishingTarget = target;
        reviewButton!.IsEnabled = group is not null;
        outcomeButton!.IsEnabled = target is not null;
        outcomeButton.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
        prepareShareButton!.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
        browserContext!.Text = target is not null ? $"Campaign target · {group?.Name} · Submit manually, then record the outcome." :
            group is not null ? $"Group review · {group.Name}" :
            "Browse Facebook or import a post from its permalink.";
        view.CoreWebView2.Navigate(url);
    }

    private static async Task OpenDefaultBrowserAsync(string url)
    {
        if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(url)))
            throw new ArgumentException("Windows could not open the default browser.");
    }

    private void CreateFacebookPage()
    {
        var layout = new Grid { Padding = new Thickness(16), RowSpacing = 8 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var toolbar = new StackPanel { Spacing = 6 };
        browserAddress = Input("Facebook address", FacebookHome);
        browserStatus = Label("");
        browserContext = Label("");
        browserAddress.Header = null;
        var navigation = new Grid { ColumnSpacing = 4 };
        for (var index = 0; index < 4; index++)
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navigation.ColumnDefinitions.Add(new ColumnDefinition());
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Task NavigateAddress()
        {
            var url = browserAddress.Text.Trim();
            if (!Validation.IsFacebookUrl(url))
                throw new ArgumentException("Enter an HTTPS Facebook URL.");
            ReadyBrowser().Navigate(url);
            return Task.CompletedTask;
        }

        void IconAction(string glyph, string description, Action action, int column)
        {
            var button = FacebookAction(navigation, description, () =>
            {
                action();
                return Task.CompletedTask;
            });
            button.Content = new FontIcon { Glyph = glyph, FontSize = 16 };
            ToolTipService.SetToolTip(button, description);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, description);
            Grid.SetColumn(button, column);
        }
        IconAction("\uE72B", "Back", () => { if (ReadyBrowser().CanGoBack) ReadyBrowser().GoBack(); }, 0);
        IconAction("\uE72A", "Forward", () => { if (ReadyBrowser().CanGoForward) ReadyBrowser().GoForward(); }, 1);
        IconAction("\uE72C", "Reload", () => ReadyBrowser().Reload(), 2);
        IconAction("\uE80F", "Facebook home", () => ReadyBrowser().Navigate(FacebookHome), 3);
        Grid.SetColumn(browserAddress, 4);
        browserAddress.PlaceholderText = "Facebook address";
        browserAddress.KeyDown += async (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter)
            {
                args.Handled = true;
                await model.RunAsync(NavigateAddress);
                if (browserStatus is not null && model.Status.Length > 0)
                    browserStatus.Text = model.Status;
            }
        };
        navigation.Children.Add(browserAddress);
        var actions = new StackPanel { Spacing = 4 };
        var toolsFlyout = new Flyout { Content = actions };
        var menu = new Button { Content = new SymbolIcon(Symbol.More), Flyout = toolsFlyout };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(menu, "Facebook tools");
        Grid.SetColumn(menu, 5);
        navigation.Children.Add(menu);
        toolbar.Children.Add(navigation);
        toolbar.Children.Add(browserStatus);
        toolbar.Children.Add(browserContext);
        FacebookAction(actions, "Continue with this session", () =>
        {
            var source = CurrentFacebookPage();
            if (Validation.IsFacebookLoginPage(source))
                throw new ArgumentException("Complete sign-in or the security check in Facebook first.");
            sessionConfirmed = true;
            browserStatus.Text = "Session confirmed by you. Continue browsing or return to GroupPilot.";
            return Task.CompletedTask;
        });
        FacebookAction(actions, "Open requested link", () =>
        {
            ReadyBrowser().Navigate(requestedFacebookUrl);
            return Task.CompletedTask;
        });
        FacebookAction(actions, "Open in default browser", () => OpenDefaultBrowserAsync(requestedFacebookUrl));
        var imports = ResponsiveGrid(toolbar, 4, 155);
        FacebookAction(imports, "Import posts", ImportCurrentPostAsync);
        FacebookAction(actions, "Import current post", () =>
        {
            toolsFlyout.Hide();
            return ImportCurrentPostAsync();
        });
        FacebookAction(imports, "Open my groups", () =>
        {
            ReadyBrowser().Navigate(FacebookGroups);
            return Task.CompletedTask;
        });
        importGroupsButton = FacebookAction(imports, "Import loaded groups", () => ImportFacebookGroupsAsync(sync: false));
        syncGroupsButton = FacebookAction(imports, "Sync groups", () => ImportFacebookGroupsAsync(sync: true));
        UpdateGroupActions();
        ToolTipService.SetToolTip(syncGroupsButton, "Use Open my groups first. Sync runs only on your Facebook groups list.");
        reviewButton = FacebookAction(actions, "Record group review", ReviewCurrentGroupAsync);
        prepareShareButton = FacebookAction(toolbar, "Prepare selected share", PrepareShareComposerAsync);
        prepareShareButton.Visibility = Visibility.Collapsed;
        outcomeButton = FacebookAction(toolbar, "Record campaign outcome", RecordBrowserOutcomeAsync);
        outcomeButton.Visibility = Visibility.Collapsed;
        reviewButton.IsEnabled = false;
        outcomeButton.IsEnabled = false;
        layout.Children.Add(toolbar);
        var view = new WebView2();
        Grid.SetRow(view, 1);
        layout.Children.Add(view);
        facebookPage = layout;
        browser = view;
    }

    private Button FacebookAction(Panel panel, string title, Func<Task> action)
    {
        var button = new Button { Content = title, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 36 };
        button.Click += async (_, _) =>
        {
            if (browser is null || model.Busy)
                return;
            await model.RunAsync(action);
            if (browserStatus is not null && model.Status.Length > 0)
                browserStatus!.Text = model.Status;
        };
        panel.Children.Add(button);
        return button;
    }

    private void ConfigureFacebookView(WebView2 view)
    {
        var core = view.CoreWebView2;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = false;
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (Validation.IsFacebookUrl(args.Uri))
                core.Navigate(args.Uri);
        };
        core.SourceChanged += (_, _) =>
        {
            if (browser == view)
                UpdateGroupActions();
        };
        view.NavigationStarting += (_, args) =>
        {
            if (browser != view)
            {
                args.Cancel = true;
                return;
            }
            args.Cancel = !Validation.IsFacebookUrl(args.Uri);
            if (!args.Cancel)
            {
                navigationId = args.NavigationId;
                pageLoaded = false;
                UpdateGroupActions();
            }
            browserStatus!.Text = args.Cancel ? "Navigation outside Facebook was blocked." : "Loading…";
        };
        view.NavigationCompleted += (sender, args) =>
        {
            if (browser != view || args.NavigationId != navigationId)
                return;

            pageLoaded = args.IsSuccess && Validation.IsFacebookUrl(view.CoreWebView2.Source);
            UpdateGroupActions();
            if (!pageLoaded)
            {
                browserStatus!.Text = $"Facebook could not load ({args.WebErrorStatus}).";
                return;
            }

            var source = view.CoreWebView2.Source;
            browserAddress!.Text = source;
            if (Validation.IsFacebookLoginPage(source))
            {
                sessionConfirmed = false;
                browserStatus!.Text = "Complete sign-in or the security check in Facebook.";
            }
            else
            {
                if (publishingTarget is not null && reviewGroup is not null && Validation.IsSameFacebookGroup(reviewGroup.Url, source))
                    _ = TryPrepareShareAfterNavigationAsync(view);
                browserStatus!.Text = sessionConfirmed ? "Using the Facebook session you confirmed." :
                    "If your account is already signed in, choose Continue with this session.";
            }
        };
    }

    private CoreWebView2 ReadyBrowser() => browserReady && browser?.CoreWebView2 is { } core ? core :
        throw new ArgumentException("Wait for the Facebook browser to start.");

    private string CurrentFacebookPage()
    {
        var source = ReadyBrowser().Source;
        if (!pageLoaded || !Validation.IsFacebookUrl(source))
            throw new ArgumentException("Wait for the Facebook page to finish loading.");
        return source;
    }

    private async Task ImportCurrentPostAsync()
    {
        var source = CurrentFacebookPage();
        if (Validation.FacebookPostImportUrl(source) is null)
        {
            await ImportFacebookPostsAsync();
            return;
        }

        var core = ReadyBrowser();
        var title = core.DocumentTitle;
        var loadedNavigation = navigationId;
        var metadataJson = await core.ExecuteScriptAsync("""
            (() => {
                const read = name => document.querySelector(`meta[property="${name}"]`)?.content?.trim() ?? "";
                return { title: read("og:title").slice(0, 300), description: read("og:description").slice(0, 4000), image: read("og:image") };
            })()
            """);
        if (browser?.CoreWebView2 != core || navigationId != loadedNavigation || CurrentFacebookPage() != source)
            throw new ArgumentException("The page changed. Open the post and import it again.");

        using var metadata = JsonDocument.Parse(metadataJson);
        var data = metadata.RootElement;
        if (data.ValueKind == JsonValueKind.Object)
        {
            var candidate = data.GetProperty("title").GetString();
            if (!string.IsNullOrWhiteSpace(candidate))
                title = candidate;
        }

        var image = data.ValueKind == JsonValueKind.Object ? data.GetProperty("image").GetString() : null;
        await PreviewFacebookPostsAsync([new Post
        {
            Kind = PostKind.Existing, Url = source,
            ThumbnailUrl = Validation.IsFacebookThumbnailUrl(image) ? image! : "",
            Title = string.IsNullOrWhiteSpace(title) ? "Facebook post" : title.Trim(),
            Body = data.ValueKind == JsonValueKind.Object ? data.GetProperty("description").GetString() ?? "" : ""
        }], "Review the existing Facebook post before saving it to the library.");
    }

    private static bool IsMyGroupsPage(string? url) =>
        url is not null && Validation.IsFacebookUrl(url)
        && new Uri(url).AbsolutePath.TrimEnd('/').Equals("/groups/joins", StringComparison.OrdinalIgnoreCase);

    private void UpdateGroupActions()
    {
        var enabled = pageLoaded && IsMyGroupsPage(browser?.CoreWebView2?.Source);
        if (importGroupsButton is not null)
            importGroupsButton.IsEnabled = enabled;
        if (syncGroupsButton is not null)
            syncGroupsButton.IsEnabled = enabled;
    }

    private async Task ImportFacebookGroupsAsync(bool sync)
    {
        var source = CurrentFacebookPage();
        if (!IsMyGroupsPage(source))
            throw new ArgumentException("Sign in, then use Open my groups before importing or syncing.");

        var core = ReadyBrowser();
        var loadedNavigation = navigationId;
        var candidates = new Dictionary<string, SocialGroup>();
        var stalled = System.Diagnostics.Stopwatch.StartNew();
        var stablePasses = 0;
        double previousHeight = -1;
        double previousTop = -1;
        var restartAtTop = sync;
        while (true)
        {
            EnsureScanPage();
            var result = await core.ExecuteScriptAsync("""
                ((scrollMode, expectedUrl) => {
                    if (location.href !== expectedUrl)
                        return null;
                    const groupPath = /^\/groups\/[a-z0-9._-]+\/?$/i;
                    const main = document.querySelector('main, [role="main"]');
                    if (!main)
                        return { Rows: [], Height: 0, Top: 0, AtBottom: false, Loading: true };
                    const links = main.querySelectorAll('a[href]');
                    const groupLinks = Array.from(links).filter(link => link.getClientRects().length && groupPath.test(link.pathname));
                    const rows = groupLinks
                        .map(link => {
                            const key = link.pathname.toLowerCase().replace(/\/$/, '');
                            let text = link.innerText;
                            let imageScope = link;
                            for (let card = link.parentElement, depth = 0; card && depth < 5; card = card.parentElement, depth++) {
                                if (card.matches('[role="main"], main') || card.innerText.length > 1200)
                                    break;
                                if (Array.from(card.querySelectorAll('a[href]')).some(other =>
                                    groupPath.test(other.pathname) && other.pathname.toLowerCase().replace(/\/$/, '') !== key))
                                    break;
                                text = card.innerText;
                                imageScope = card;
                            }
                            const image = Array.from(imageScope.querySelectorAll('img')).find(img => {
                                const source = img.currentSrc || img.src;
                                return img.naturalWidth >= 40 && img.naturalHeight >= 40 &&
                                    /^https:\/\/[^/]+\.fbcdn\.net\//i.test(source);
                            });
                            return {
                                Url: link.href,
                                FacebookImageUrl: image?.currentSrc || image?.src || '',
                                Name: (link.innerText.split('\n').find(line => line.trim()) ?? link.getAttribute('aria-label') ?? '').trim().slice(0, 250),
                                FacebookCardText: text.trim().slice(0, 1200)
                            };
                        });
                    let scroller = groupLinks.at(-1)?.parentElement;
                    while (scroller && !(/auto|scroll|overlay/.test(getComputedStyle(scroller).overflowY)
                        && scroller.scrollHeight > scroller.clientHeight + 2))
                        scroller = scroller.parentElement;
                    scroller ??= document.scrollingElement;
                    const height = scroller?.scrollHeight ?? 0;
                    const top = scroller?.scrollTop ?? 0;
                    const atBottom = scroller && height - scroller.clientHeight - top < 4;
                    const loading = main.getAttribute('aria-busy') === 'true' ||
                        Array.from(main.querySelectorAll('[aria-busy="true"], [role="progressbar"]'))
                            .some(element => element.getClientRects().length > 0);
                    if (scrollMode && scroller) {
                        // Walk the list rather than skipping virtualized rows between viewports.
                        const next = scrollMode === 2 ? 0 : Math.min(height, scroller.scrollTop + Math.max(1, scroller.clientHeight * 0.85));
                        scroller.scrollTo({ top: next, behavior: 'instant' });
                    }
                    return { Rows: rows, Height: height, Top: top, AtBottom: !!atBottom, Loading: loading };
                })
                """ + $"({(restartAtTop ? 2 : sync ? 1 : 0)}, {JsonSerializer.Serialize(source)})");
            EnsureScanPage();
            var scan = JsonSerializer.Deserialize<FacebookGroupScan>(result)
                ?? throw new ArgumentException("Facebook did not return a group list. Try again.");
            var previousCount = candidates.Count;
            foreach (var row in scan.Rows)
            {
                if (string.IsNullOrWhiteSpace(row.Url))
                    continue;
                string url;
                try
                {
                    url = Validation.GroupUrl(row.Url);
                }
                catch (ArgumentException)
                {
                    continue;
                }
                row.Url = url;
                FacebookGroupMetadata.Apply(row, row.FacebookCardText);
                if (!candidates.TryGetValue(url, out var previous))
                {
                    candidates[url] = row;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(previous.Name))
                    previous.Name = row.Name;
                previous.MemberCount ??= row.MemberCount;
                if (!Validation.IsFacebookThumbnailUrl(previous.FacebookImageUrl)
                    && Validation.IsFacebookThumbnailUrl(row.FacebookImageUrl))
                    previous.FacebookImageUrl = row.FacebookImageUrl;
                if (previous.FacebookVisibility.Length == 0)
                    previous.FacebookVisibility = row.FacebookVisibility;
                if (previous.FacebookActivity.Length == 0)
                    previous.FacebookActivity = row.FacebookActivity;
                if (row.FacebookCardText.Length > previous.FacebookCardText.Length)
                    previous.FacebookCardText = row.FacebookCardText;
            }

            if (!sync)
                break;

            var advanced = candidates.Count != previousCount || scan.Height != previousHeight || scan.Top != previousTop;
            if (advanced)
                stalled.Restart();
            stablePasses = !restartAtTop && scan.AtBottom && !scan.Loading && !advanced ? stablePasses + 1 : 0;
            browserStatus!.Text = $"Scanning Facebook groups… {candidates.Count} found.";
            if (stablePasses >= 4)
                break;
            if (stalled.Elapsed >= TimeSpan.FromSeconds(45))
                throw new ArgumentException("The group list stopped making progress. Sync was stopped before any groups were removed.");
            previousHeight = scan.Height;
            previousTop = scan.Top;
            restartAtTop = false;
            await Task.Delay(1250);
        }

        void EnsureScanPage()
        {
            if (browser?.CoreWebView2 != core || navigationId != loadedNavigation || CurrentFacebookPage() != source)
                throw new ArgumentException("The page changed. Open your groups and try again.");
        }

        if (sync)
        {
            await PreviewFacebookSyncAsync(candidates);
            return;
        }

        var known = model.Groups.Select(group => group.Url).ToHashSet();
        var available = candidates.Values.Where(group => !known.Contains(group.Url)).ToArray();
        if (available.Length == 0)
            throw new ArgumentException(candidates.Count == 0
                ? "No group links found. Wait for your groups to load or scroll and try again."
                : "All detected groups are already saved.");

        var choices = List(available.Select(group => new FacebookGroupChoice(group)).ToArray(), ListViewSelectionMode.Multiple);
        choices.SelectAll();
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(Label($"{available.Length} new links from the loaded page. Review the selection before saving."));
        content.Children.Add(choices);
        Activate();
        var dialog = Dialog("Import Facebook groups", content, "Save selected");
        dialog.PrimaryButtonClick += (_, args) => args.Cancel = choices.SelectedItems.Count == 0;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var imported = await storage.ImportGroupsAsync(choices.SelectedItems.Cast<FacebookGroupChoice>().Select(choice => choice.Group));
        await model.ReloadAsync();
        NavigateTo("Groups");
        if (browserStatus is not null)
            browserStatus!.Text = $"Saved {imported.Added} groups. Skipped {imported.Skipped}.";
    }

    private async Task PreviewFacebookSyncAsync(Dictionary<string, SocialGroup> scanned)
    {
        if (scanned.Count == 0)
            throw new ArgumentException("No group links found. Wait for the list to load and try again.");

        var expected = model.Groups.ToDictionary(group => group.Id, group => group.Url);
        var known = expected.Values.ToHashSet();
        var added = scanned.Values.Where(group => !known.Contains(group.Url)).ToArray();
        var removed = model.Groups.Where(group => !scanned.ContainsKey(group.Url)).ToArray();
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(Label($"Add {added.Length} · Refresh {scanned.Count - added.Length} · Remove {removed.Length}"));
        content.Children.Add(Label("Sync uses the groups Facebook returned during the scan. Check the removals before applying. Removed groups become unavailable for existing campaign targets. Publishing history is kept."));
        if (added.Length > 0)
        {
            content.Children.Add(Label("Groups to add"));
            content.Children.Add(List(added, ListViewSelectionMode.None));
        }
        if (removed.Length > 0)
        {
            content.Children.Add(Label("Groups to remove"));
            content.Children.Add(List(removed, ListViewSelectionMode.None));
        }

        var confirm = new CheckBox { Content = "I reviewed the changes and want to apply this scan." };
        content.Children.Add(confirm);
        Activate();
        var dialog = Dialog("Sync Facebook groups", new ScrollViewer { Content = content, MaxHeight = 460 }, "Apply sync");
        dialog.IsPrimaryButtonEnabled = false;
        confirm.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
        confirm.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var result = await storage.SyncFacebookGroupsAsync(scanned.Values, expected);
        await model.ReloadAsync();
        NavigateTo("Groups");
        if (browserStatus is not null)
            browserStatus!.Text = $"Sync complete. Added {result.Added}, refreshed {result.Updated}, removed {result.Removed}.";
    }

    private sealed record FacebookGroupScan(SocialGroup[] Rows, double Height, double Top, bool AtBottom, bool Loading);

    private sealed record FacebookGroupChoice(SocialGroup Group)
    {
        public override string ToString() => string.IsNullOrWhiteSpace(Group.Name) ? Group.Url : $"{Group} · {Group.Url}";
    }

    private async Task ReviewCurrentGroupAsync()
    {
        var group = reviewGroup ?? throw new ArgumentException("Open a saved group using Review in Facebook.");
        var source = CurrentFacebookPage();
        if (!Validation.IsSameFacebookGroup(group.Url, source))
            throw new ArgumentException("Return to the saved group first. If Facebook redirects to a different group link, update the saved URL.");
        Activate();

        var fields = new StackPanel { Spacing = 10 };
        fields.Children.Add(Label("Check the group rules and whether the signed-in account can post. Save what you observed."));
        if (group.ReviewedAt is { } reviewed)
            fields.Children.Add(Label($"Last manual review: {reviewed.ToLocalTime():g}"));
        var access = Choices("Posting access", new[]
        {
            GroupPostingAccess.Allowed, GroupPostingAccess.RequiresApproval, GroupPostingAccess.Unavailable
        }, group.PostingAccess);
        var rules = Input("Rules observed in Facebook", group.Rules);
        rules.AcceptsReturn = true;
        rules.TextWrapping = TextWrapping.Wrap;
        rules.MinHeight = 110;
        fields.Children.Add(access);
        fields.Children.Add(rules);
        var dialog = Dialog($"Review {group.Name}", fields, "Save review");
        dialog.IsPrimaryButtonEnabled = access.SelectedItem is not null;
        access.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = access.SelectedItem is not null;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || access.SelectedItem is not GroupPostingAccess selected)
            return;

        await storage.SaveGroupReviewAsync(group.Id, group.Url, selected, rules.Text);
        await model.ReloadAsync();
        reviewGroup = model.Groups.Single(item => item.Id == group.Id);
        if (publishingTarget is { } context)
        {
            NavigateTo("Campaigns");
            ShowCampaignDetails(model.Campaigns.Single(item => item.Id == context.CampaignId && !item.IsArchived));
        }
        else
        {
            NavigateTo("Groups");
        }
        if (browserStatus is not null)
            browserStatus!.Text = "Group review saved.";
    }

    private async Task RecordBrowserOutcomeAsync()
    {
        var context = publishingTarget ?? throw new ArgumentException("Open a campaign target first.");
        var campaign = model.Campaigns.Single(item => item.Id == context.CampaignId);
        var target = campaign.Targets.Single(item => item.Id == context.TargetId);
        var source = browser?.CoreWebView2?.Source;
        var suggestedUrl = pageLoaded && source is not null && Validation.IsFacebookPostUrl(source)
            && Validation.IsSameFacebookGroup(target.GroupUrl, source) ? source : "";
        Activate();
        NavigateTo("Campaigns");
        ShowCampaignDetails(campaign);
        await RecordOutcomeAsync(campaign, target, suggestedUrl);
    }
}

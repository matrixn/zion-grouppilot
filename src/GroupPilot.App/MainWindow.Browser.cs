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
    private Window? facebookWindow;
    private WebView2? browser;
    private TextBox? browserAddress;
    private TextBlock? browserStatus;
    private TextBlock? browserContext;
    private Button? reviewButton;
    private Button? outcomeButton;
    private bool pageLoaded;
    private ulong navigationId;
    private bool browserReady;
    private bool sessionConfirmed;
    private string requestedFacebookUrl = FacebookHome;
    private SocialGroup? reviewGroup;
    private (int CampaignId, int TargetId)? publishingTarget;

    private void ShowBrowser()
    {
        var page = Page("Facebook Browser", "Facebook opens in its own window and reuses the GroupPilot browser profile.");
        var session = Section(page, "Facebook session");
        session.Children.Add(Label("If Facebook is already signed in here, continue with that account. Otherwise sign in in the Facebook window."));
        var actions = ResponsiveGrid(session, 2, 205);
        Action(actions, "Open Facebook window", () => OpenFacebookAsync(FacebookHome, keepCurrentPage: true), primary: true);
        Action(actions, "Open in default browser", () => OpenDefaultBrowserAsync(FacebookHome));
    }

    private async Task OpenFacebookAsync(string url, SocialGroup? group = null,
        (int CampaignId, int TargetId)? target = null, bool keepCurrentPage = false)
    {
        if (!Validation.IsFacebookUrl(url))
            throw new ArgumentException("Enter an HTTPS Facebook URL.");

        if (facebookWindow is null)
            CreateFacebookWindow();
        var window = facebookWindow!;
        var view = browser!;
        window.Activate();
        if (!browserReady)
        {
            browserStatus!.Text = "Starting the local Facebook profile…";
            try
            {
                var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                    null, Path.Combine(App.DataDirectory, "browser"), new CoreWebView2EnvironmentOptions());
                if (facebookWindow != window)
                    return;
                await view.EnsureCoreWebView2Async(environment);
                if (facebookWindow != window)
                    return;
                ConfigureFacebookView(view);
                browserReady = true;
            }
            catch (Exception) when (facebookWindow != window)
            {
                return;
            }
            catch
            {
                window.Close();
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

    private void CreateFacebookWindow()
    {
        var window = new Window { Title = "GroupPilot · Facebook" };
        window.AppWindow.SetIcon("Assets/AppIcon.ico");
        var layout = new Grid { Padding = new Thickness(16), RowSpacing = 8 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var toolbar = new StackPanel { Spacing = 6 };
        browserAddress = Input("Facebook address", FacebookHome);
        browserStatus = Label("");
        browserContext = Label("");
        toolbar.Children.Add(browserAddress);
        toolbar.Children.Add(browserStatus);
        toolbar.Children.Add(browserContext);
        var actions = ResponsiveGrid(toolbar, 3, 165);
        FacebookAction(actions, "Open address", () =>
        {
            var url = browserAddress.Text.Trim();
            if (!Validation.IsFacebookUrl(url))
                throw new ArgumentException("Enter an HTTPS Facebook URL.");
            ReadyBrowser().Navigate(url);
            return Task.CompletedTask;
        });
        FacebookAction(actions, "Back", () =>
        {
            ReadyBrowser();
            if (browser?.CanGoBack == true)
                browser.GoBack();
            return Task.CompletedTask;
        });
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
        FacebookAction(actions, "Import current post", ImportCurrentPostAsync);
        FacebookAction(actions, "Open my groups", () =>
        {
            ReadyBrowser().Navigate(FacebookGroups);
            return Task.CompletedTask;
        });
        FacebookAction(actions, "Import loaded groups", ImportFacebookGroupsAsync);
        reviewButton = FacebookAction(actions, "Record group review", ReviewCurrentGroupAsync);
        outcomeButton = FacebookAction(actions, "Record campaign outcome", RecordBrowserOutcomeAsync);
        reviewButton.IsEnabled = false;
        outcomeButton.IsEnabled = false;
        FacebookAction(actions, "Return to GroupPilot", () =>
        {
            Activate();
            return Task.CompletedTask;
        });
        layout.Children.Add(toolbar);
        var view = new WebView2();
        Grid.SetRow(view, 1);
        layout.Children.Add(view);
        window.Content = layout;
        facebookWindow = window;
        browser = view;
        window.Closed += (_, _) =>
        {
            facebookWindow = null;
            browserReady = false;
            pageLoaded = false;
            sessionConfirmed = false;
            reviewGroup = null;
            publishingTarget = null;
            browser = null;
            browserAddress = null;
            browserStatus = null;
            browserContext = null;
            reviewButton = null;
            outcomeButton = null;
            view.Close();
        };
    }

    private Button FacebookAction(Panel panel, string title, Func<Task> action)
    {
        var button = new Button { Content = title, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 36 };
        button.Click += async (_, _) =>
        {
            var window = facebookWindow;
            if (window is null || model.Busy)
                return;
            await model.RunAsync(action);
            if (facebookWindow == window && model.Status.Length > 0)
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
            }
            browserStatus!.Text = args.Cancel ? "Navigation outside Facebook was blocked." : "Loading…";
        };
        view.NavigationCompleted += (_, args) =>
        {
            if (browser != view || args.NavigationId != navigationId)
                return;

            pageLoaded = args.IsSuccess && Validation.IsFacebookUrl(view.CoreWebView2.Source);
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
        if (!Validation.IsFacebookPostUrl(source))
            throw new ArgumentException("Open a Facebook post permalink first.");

        var core = ReadyBrowser();
        var title = core.DocumentTitle;
        var loadedNavigation = navigationId;
        var metadataJson = await core.ExecuteScriptAsync("""
            (() => {
                const read = name => document.querySelector(`meta[property="${name}"]`)?.content?.trim() ?? "";
                return { title: read("og:title").slice(0, 300), description: read("og:description").slice(0, 4000) };
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

        Activate();
        await Edit(new Post
        {
            Kind = PostKind.Existing, Url = source,
            Title = string.IsNullOrWhiteSpace(title) ? "Facebook post" : title.Trim(),
            Body = data.ValueKind == JsonValueKind.Object ? data.GetProperty("description").GetString() ?? "" : ""
        });
    }

    private async Task ImportFacebookGroupsAsync()
    {
        var source = CurrentFacebookPage();
        if (!new Uri(source).AbsolutePath.TrimEnd('/').Equals("/groups/joins", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Sign in, then use Open my groups. Scroll to load more groups before importing.");

        var core = ReadyBrowser();
        var loadedNavigation = navigationId;
        var result = await core.ExecuteScriptAsync("""
            Array.from((document.querySelector('[role="main"]') ?? document).querySelectorAll('a[href]'))
                .filter(link => link.getClientRects().length && /^\/groups\/[a-z0-9._-]+\/?$/i.test(link.pathname))
                .map(link => ({
                    Url: link.href,
                    Name: (link.innerText.split('\n').find(line => line.trim()) ?? link.getAttribute('aria-label') ?? '').trim().slice(0, 250)
                }))
            """);
        if (browser?.CoreWebView2 != core || navigationId != loadedNavigation || CurrentFacebookPage() != source)
            throw new ArgumentException("The page changed. Open your groups and try again.");

        var candidates = new Dictionary<string, SocialGroup>();
        foreach (var row in JsonSerializer.Deserialize<SocialGroup[]>(result) ?? [])
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
            if (!candidates.TryGetValue(url, out var previous) || string.IsNullOrWhiteSpace(previous.Name))
                candidates[url] = new SocialGroup { Url = url, Name = row.Name };
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
        if (facebookWindow is not null)
            browserStatus!.Text = $"Saved {imported.Added} groups. Skipped {imported.Skipped}.";
    }

    private sealed record FacebookGroupChoice(SocialGroup Group)
    {
        public override string ToString() => string.IsNullOrWhiteSpace(Group.Name) ? Group.Url : $"{Group.Name} · {Group.Url}";
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
            ShowCampaignDetails(model.Campaigns.Single(item => item.Id == context.CampaignId));
        }
        else
        {
            NavigateTo("Groups");
        }
        if (facebookWindow is not null)
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

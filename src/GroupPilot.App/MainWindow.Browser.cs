using GroupPilot.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace GroupPilot_App;

public sealed partial class MainWindow
{
    private Grid? browserPage;
    private TextBox? browserAddress;
    private TextBlock? browserStatus;

    private void ShowBrowser()
    {
        if (browserPage is not null)
        {
            ContentHost.Content = browserPage;
            return;
        }

        browserPage = new Grid { Padding = new Thickness(16), RowSpacing = 8 };
        browserPage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        browserPage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var toolbar = new StackPanel { Spacing = 6 };
        toolbar.Children.Add(Label("Sign in using Facebook. The browser keeps a separate local profile."));
        browserAddress = Input("Facebook address", "https://www.facebook.com/");
        browserStatus = Label("Open Facebook to initialize the local browser profile.");
        toolbar.Children.Add(browserAddress);
        toolbar.Children.Add(browserStatus);
        Action(toolbar, "Open address", () => OpenFacebookAsync(browserAddress.Text.Trim()));
        Action(toolbar, "Draft from this page", () =>
        {
            var source = browser?.Source?.AbsoluteUri;
            if (source is null || !Validation.IsFacebookPostUrl(source))
                throw new ArgumentException("Open a Facebook post permalink first.");
            var title = browser?.CoreWebView2.DocumentTitle;
            return Edit(new Post
            {
                Kind = PostKind.Existing, Url = source,
                Title = string.IsNullOrWhiteSpace(title) ? "Facebook post" : title.Trim()
            });
        });
        Action(toolbar, "Back in browser", () =>
        {
            if (browser?.CanGoBack == true)
                browser.GoBack();
            return Task.CompletedTask;
        });
        Action(toolbar, "Return to active campaign", () =>
        {
            NavigateTo("Campaigns");
            if (activeCampaign is not null)
                ShowCampaignDetails(activeCampaign);
            return Task.CompletedTask;
        });
        browserPage.Children.Add(toolbar);
        ContentHost.Content = browserPage;
    }

    private async Task OpenFacebookAsync(string url)
    {
        if (!Validation.IsFacebookUrl(url))
            throw new ArgumentException("Enter an HTTPS Facebook URL.");
        NavigateTo("Browser");
        if (browser is null)
        {
            browserStatus!.Text = "Starting the isolated browser…";
            var view = new WebView2();
            Grid.SetRow(view, 1);
            browserPage!.Children.Add(view);
            try
            {
                var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                    null, Path.Combine(App.DataDirectory, "browser"), new CoreWebView2EnvironmentOptions());
                await view.EnsureCoreWebView2Async(environment);
                view.CoreWebView2.Settings.AreHostObjectsAllowed = false;
                view.CoreWebView2.Settings.IsWebMessageEnabled = false;
                view.CoreWebView2.NewWindowRequested += (_, args) =>
                {
                    args.Handled = true;
                    if (Validation.IsFacebookUrl(args.Uri))
                        view.CoreWebView2.Navigate(args.Uri);
                };
                view.NavigationStarting += (_, args) =>
                {
                    args.Cancel = !Validation.IsFacebookUrl(args.Uri);
                    browserStatus.Text = args.Cancel ? "Navigation outside Facebook was blocked." : "Loading…";
                };
                view.NavigationCompleted += (_, args) =>
                {
                    browserStatus.Text = args.IsSuccess ? "" : $"Facebook could not load ({args.WebErrorStatus}).";
                    if (args.IsSuccess && view.Source is not null)
                        browserAddress!.Text = view.Source.AbsoluteUri;
                };
                browser = view;
            }
            catch
            {
                browserPage.Children.Remove(view);
                view.Close();
                browserStatus.Text = "Could not start WebView2. Verify the runtime and try opening the address again.";
                throw;
            }
        }
        browser.CoreWebView2.Navigate(url);
    }
}

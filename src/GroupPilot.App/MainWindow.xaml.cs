using GroupPilot.Core;
using GroupPilot.Infrastructure;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GroupPilot_App;

public sealed partial class MainWindow : Window
{
    private readonly Storage storage;
    private readonly PilotViewModel model;
    private bool initialized;
    private Post? editingPost;
    private WebView2? browser;

    public MainWindow(Storage storage, PilotViewModel model)
    {
        this.storage = storage;
        this.model = model;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        model.PropertyChanged += (_, _) =>
        {
            Feedback.Message = model.Status;
            Feedback.IsOpen = model.Status.Length > 0;
            ContentHost.IsEnabled = !model.Busy;
            Navigation.IsEnabled = initialized && !model.Busy;
        };
        Root.Loaded += Initialize;
    }

    private async void Initialize(object sender, RoutedEventArgs args)
    {
        Root.Loaded -= Initialize;
        await StartAsync();
    }

    private Task StartAsync() => model.RunAsync(async () =>
    {
        try
        {
            await storage.InitializeAsync();
            await model.ReloadAsync();
            ApplyTheme(model.Theme);
            initialized = true;
            NavigateTo("Dashboard");
            Serilog.Log.Information("GroupPilot dashboard ready");
        }
        catch
        {
            initialized = false;
            var panel = Page("Startup unavailable", "Retry initializing GroupPilot.");
            var retry = new Button { Content = "Retry" };
            retry.Click += async (_, _) => await StartAsync();
            panel.Children.Add(retry);
            throw;
        }
    });

    private void TogglePane(TitleBar sender, object args) => Navigation.IsPaneOpen = !Navigation.IsPaneOpen;

    private void Navigate(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!initialized)
            return;
        Show(args.IsSettingsSelected ? "Settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string);
    }

    private void NavigateTo(string page)
    {
        var item = Navigation.MenuItems.OfType<NavigationViewItem>().Single(item => Equals(item.Tag, page));
        if (ReferenceEquals(Navigation.SelectedItem, item))
            Show(page);
        else
            Navigation.SelectedItem = item;
    }

    private void Show(string? page)
    {
        switch (page)
        {
            case "Dashboard": ShowDashboard(); break;
            case "Library": ShowLibrary(); break;
            case "Composer": ShowComposer(editingPost); break;
            case "Groups": ShowGroups(); break;
            case "Campaigns": ShowCampaigns(); break;
            case "Browser": ShowBrowser(); break;
            case "History": ShowHistory(); break;
            case "Settings": ShowSettings(); break;
        }
    }

    private StackPanel Page(string heading, string description)
    {
        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(24), MaxWidth = 920 };
        panel.Children.Add(new TextBlock { Text = heading, Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] });
        panel.Children.Add(Label(description));
        ContentHost.Content = new ScrollViewer { Content = panel };
        return panel;
    }

    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
    private static TextBox Input(string header, string text = "") => new() { Header = header, Text = text };

    private static ComboBox Choices<T>(string header, IEnumerable<T> items, T? selected = default)
    {
        var box = new ComboBox { Header = header, ItemsSource = items };
        box.SelectedItem = selected;
        return box;
    }

    private static ListView List<T>(IEnumerable<T> items, ListViewSelectionMode mode = ListViewSelectionMode.Single) =>
        new() { ItemsSource = items, SelectionMode = mode, MaxHeight = 300 };

    private void Action(Panel panel, string title, Func<Task> handler, string success = "")
    {
        var button = new Button { Content = title };
        button.Click += async (_, _) => await model.RunAsync(handler, success);
        panel.Children.Add(button);
    }

    private async Task<string?> PickFileAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        foreach (var extension in extensions)
            picker.FileTypeFilter.Add(extension);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    private ContentDialog Dialog(string title, object content, string action) => new()
    {
        XamlRoot = Root.XamlRoot, Title = title, Content = content,
        PrimaryButtonText = action, CloseButtonText = "Cancel"
    };

    private void ApplyTheme(string theme) => Root.RequestedTheme = Enum.Parse<ElementTheme>(theme);

    private void ShowDashboard()
    {
        var page = Page("Dashboard", "Prepare posts, organize groups and track assisted publishing on this PC.");
        var counts = new StackPanel { Spacing = 8 };
        counts.Children.Add(Label($"{model.Posts.Count} saved posts"));
        counts.Children.Add(Label($"{model.Groups.Count} saved groups"));
        counts.Children.Add(Label($"{model.Campaigns.Count} saved campaigns"));
        page.Children.Add(new Border
        {
            Style = (Style)Application.Current.Resources["DashboardCardStyle"],
            Child = counts
        });
        Action(page, "Compose a post", () =>
        {
            editingPost = null;
            NavigateTo("Composer");
            return Task.CompletedTask;
        });
    }

    private void ShowHistory()
    {
        var page = Page("Publishing History", "Outcomes recorded by the user after working in Facebook.");
        page.Children.Add(model.History.Count == 0 ? Label("No publishing attempts yet.") : List(model.History));
    }

    private void ShowSettings()
    {
        var page = Page("Settings", "Preferences and data are stored locally.");
        var theme = Choices("Appearance", Enum.GetNames<ElementTheme>(), model.Theme);
        page.Children.Add(theme);
        Action(page, "Save appearance", async () =>
        {
            var value = (string)theme.SelectedItem;
            await storage.SaveAsync(async db =>
            {
                var settings = await db.Settings.FindAsync(1);
                if (settings is null)
                    db.Settings.Add(new AppSettings { Id = 1, Theme = value });
                else
                    settings.Theme = value;
            });
            await model.ReloadAsync();
            ApplyTheme(value);
        }, "Appearance saved.");
        page.Children.Add(Label($"Local data: {App.DataDirectory}"));
    }
}

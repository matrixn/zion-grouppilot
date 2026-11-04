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

    public MainWindow(Storage storage, PilotViewModel model)
    {
        this.storage = storage;
        this.model = model;
        InitializeComponent();
        var version = typeof(MainWindow).Assembly.GetName().Version!;
        Title = $"GroupPilot v{version.Major}.{version.Minor:00}.{version.Build:00}";
        AppTitleBar.Title = Title;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        model.PropertyChanged += (_, _) =>
        {
            Feedback.Message = model.Status;
            Feedback.IsOpen = model.Status.Length > 0;
            ContentHost.IsEnabled = !model.Busy;
            FacebookHost.IsEnabled = !model.Busy;
            Navigation.IsEnabled = initialized && !model.Busy;
        };
        Root.Loaded += Initialize;
        Closed += (_, _) =>
        {
            campaignReminderTimer?.Stop();
            var view = browser;
            browser = null;
            view?.Close();
        };
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
            StartCampaignReminders();
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
        FacebookHost.Visibility = page == "Browser" ? Visibility.Visible : Visibility.Collapsed;
        ContentHost.Visibility = page == "Browser" ? Visibility.Collapsed : Visibility.Visible;
        switch (page)
        {
            case "Dashboard": ShowDashboard(); break;
            case "Library": ShowLibrary(); break;
            case "Groups": ShowGroups(); break;
            case "Campaigns": ShowCampaigns(); break;
            case "Browser": ShowBrowser(); break;
            case "History": ShowHistory(); break;
            case "Settings": ShowSettings(); break;
        }
    }

    private StackPanel Page(string heading, string description)
    {
        var panel = new StackPanel { Spacing = 18, Padding = new Thickness(24, 24, 24, 36) };
        var header = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new TextBlock
        {
            Text = heading, TextWrapping = TextWrapping.Wrap, FontSize = 32,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock { Text = description, Style = ResourceStyle("WorkspaceDescriptionStyle") });
        panel.Children.Add(header);
        ContentHost.Content = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = panel
        };
        return panel;
    }

    private static Style ResourceStyle(string key) => (Style)Application.Current.Resources[key];

    private static StackPanel Section(Panel parent, string heading)
    {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = heading, FontSize = 18, TextWrapping = TextWrapping.Wrap,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        parent.Children.Add(new Border { Style = ResourceStyle("WorkspaceCardStyle"), Child = content });
        return content;
    }

    private static Grid ResponsiveGrid(Panel parent, int maxColumns, double minimumColumnWidth, params UIElement[] children)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        foreach (var child in children)
            grid.Children.Add(child);

        var currentColumns = 0;
        void Arrange(double width)
        {
            var columns = Math.Clamp((int)((width + grid.ColumnSpacing) /
                (minimumColumnWidth + grid.ColumnSpacing)), 1, maxColumns);
            if (columns == currentColumns && grid.RowDefinitions.Count == (grid.Children.Count + columns - 1) / columns)
                return;
            currentColumns = columns;
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            for (var column = 0; column < columns; column++)
                grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (var index = 0; index < grid.Children.Count; index++)
            {
                if (index % columns == 0)
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var child = (FrameworkElement)grid.Children[index];
                Grid.SetRow(child, index / columns);
                Grid.SetColumn(child, index % columns);
            }
        }

        grid.Loaded += (_, _) => Arrange(grid.ActualWidth);
        grid.SizeChanged += (_, args) => Arrange(args.NewSize.Width);
        parent.Children.Add(grid);
        return grid;
    }

    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
    private static TextBox Input(string header, string text = "") => new() { Header = header, Text = text };

    private static ComboBox Choices<T>(string header, IEnumerable<T> items, T? selected = default)
    {
        var box = new ComboBox { Header = header, ItemsSource = items, HorizontalAlignment = HorizontalAlignment.Stretch };
        box.SelectedItem = selected;
        return box;
    }

    private static ListView List<T>(IEnumerable<T> items, ListViewSelectionMode mode = ListViewSelectionMode.Single) =>
        new() { ItemsSource = items, SelectionMode = mode, MaxHeight = 300 };

    private void Action(Panel panel, string title, Func<Task> handler, string success = "", bool primary = false)
    {
        var button = new Button { Content = title };
        if (primary)
            button.Style = ResourceStyle("WorkspacePrimaryButtonStyle");
        if (panel is Grid)
        {
            button.MinHeight = 36;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
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
        var summary = ResponsiveGrid(page, 3, 180);
        void Metric(string label, int count)
        {
            var card = Section(summary, label);
            card.Children.Add(new TextBlock { Text = count.ToString(), FontSize = 34 });
        }
        Metric("Saved posts", model.Posts.Count(IsLibraryPost));
        Metric("Saved groups", model.Groups.Count);
        Metric("Campaigns", model.Campaigns.Count(campaign => !campaign.IsArchived));

        var workspace = Section(page, "Quick actions");
        workspace.Children.Add(Label("Import existing Facebook posts or manage your groups and campaigns."));
        var actions = ResponsiveGrid(workspace, 3, 168);
        Action(actions, "Open Facebook Browser", () => OpenPage("Browser"), primary: true);
        Action(actions, "Manage groups", () => OpenPage("Groups"));
        Action(actions, "View campaigns", () => OpenPage("Campaigns"));
    }

    private Task OpenPage(string page)
    {
        NavigateTo(page);
        return Task.CompletedTask;
    }

    private void ShowHistory()
    {
        var page = Page("Publishing History", "Outcomes recorded by the user after working in Facebook.");
        Section(page, "Publishing activity").Children.Add(model.History.Count == 0 ? Label("No publishing attempts yet.") : List(model.History));
    }

    private void ShowSettings()
    {
        var page = Page("Settings", "Preferences and data are stored locally.");
        var appearance = Section(page, "Appearance");
        var theme = Choices("Theme", Enum.GetNames<ElementTheme>(), model.Theme);
        appearance.Children.Add(theme);
        Action(appearance, "Save appearance", async () =>
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
        }, "Appearance saved.", primary: true);
        Section(page, "Local storage").Children.Add(new TextBlock
        {
            Text = App.DataDirectory, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true
        });
    }
}

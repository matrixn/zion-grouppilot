using GroupPilot.Core;
using Microsoft.UI.Xaml.Controls;

namespace GroupPilot_App;

public sealed partial class MainWindow
{
    private void ShowGroups()
    {
        var page = Page("Groups", "Manage Facebook group URLs, categories and posting rules.");
        var directory = Section(page, "Group directory");
        var search = Input("Search groups");
        var categories = Choices("Category", new[] { "All categories" }.Concat(
            model.Groups.Select(group => group.Category?.Name).OfType<string>().Distinct().Order()), "All categories");
        var visibility = Choices("Facebook visibility", new[] { "Any", "Public", "Private", "Unknown" }, "Any");
        var minimumMembers = new NumberBox { Header = "Minimum members", Minimum = 0, PlaceholderText = "Any" };
        var groups = List(model.Groups);
        var empty = Label("");
        void Filter()
        {
            var matches = model.Groups.Where(group =>
                (double.IsNaN(minimumMembers.Value) || (group.MemberCount is { } members && members >= minimumMembers.Value)) &&
                (visibility.SelectedIndex <= 0 || Equals(visibility.SelectedItem,
                    group.FacebookVisibility.Length == 0 ? "Unknown" : group.FacebookVisibility)) &&
                (categories.SelectedIndex <= 0 || Equals(group.Category?.Name, categories.SelectedItem)) &&
                (group.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) ||
                 group.Url.Contains(search.Text, StringComparison.OrdinalIgnoreCase))).ToArray();
            groups.ItemsSource = matches;
            empty.Text = matches.Length == 0 ? "No groups match. Add a group or import a CSV." : "";
        }

        ResponsiveGrid(directory, 4, 170, search, categories, visibility, minimumMembers);
        directory.Children.Add(empty);
        directory.Children.Add(groups);
        search.TextChanged += (_, _) => Filter();
        categories.SelectionChanged += (_, _) => Filter();
        visibility.SelectionChanged += (_, _) => Filter();
        minimumMembers.ValueChanged += (_, _) => Filter();
        var details = Label("");
        directory.Children.Add(details);
        groups.SelectionChanged += (_, _) =>
        {
            details.Text = groups.SelectedItem is SocialGroup group && group.FacebookScannedAt is { } scanned
                ? $"Facebook scan: {scanned.ToLocalTime():g}\n{group.FacebookCardText}" : "";
        };
        Filter();

        SocialGroup Selected() => groups.SelectedItem as SocialGroup ?? throw new ArgumentException("Select a group first.");
        var actions = ResponsiveGrid(directory, 4, 160);
        Action(actions, "Add group", () => EditGroupAsync(null), primary: true);
        Action(actions, "Edit selected group", () => EditGroupAsync(Selected()));
        Action(actions, "Review in Facebook", () =>
        {
            var group = Selected();
            return OpenFacebookAsync(group.Url, group: group);
        });
        Action(actions, "Delete selected group", async () =>
        {
            var group = Selected();
            if (await Dialog("Delete group?", Label($"Remove {group.Name} from the group library? Campaign history is kept."), "Delete").ShowAsync()
                != ContentDialogResult.Primary)
                return;
            await storage.SaveAsync(async db =>
            {
                var existing = await db.Groups.FindAsync(group.Id);
                if (existing is not null)
                    db.Groups.Remove(existing);
            });
            await model.ReloadAsync();
            ShowGroups();
        });
        var sync = Section(page, "Facebook sync");
        sync.Children.Add(Label("Open your groups, load the complete list and choose Sync groups to preview additions and removals."));
        Action(sync, "Open my groups in Facebook", () => OpenFacebookAsync(FacebookGroups), primary: true);
        var importResult = Label("CSV columns: name,url. A URL-only file also works.");
        var import = Section(page, "Import groups");
        import.Children.Add(importResult);
        Action(import, "Import CSV", async () =>
        {
            if (await PickFileAsync(".csv") is not { } path)
                return;
            var result = await storage.ImportGroupsAsync(path);
            await model.ReloadAsync();
            Filter();
            importResult.Text = $"Imported {result.Added}; skipped {result.Skipped} invalid or duplicate rows.";
        });
    }

    private async Task EditGroupAsync(SocialGroup? source)
    {
        var fields = new StackPanel { Spacing = 12 };
        var name = Input("Name", source?.Name ?? "");
        var url = Input("Facebook group URL", source?.Url ?? "");
        var category = Input("Category", source?.Category?.Name ?? "");
        var notes = Input("Notes", source?.Notes ?? "");
        var rules = Input("Posting rules", source?.Rules ?? "");
        var enabled = new CheckBox { Content = "Available for campaigns", IsChecked = source?.Enabled ?? true };
        foreach (var control in new Microsoft.UI.Xaml.UIElement[] { name, url, category, notes, rules, enabled })
            fields.Children.Add(control);
        var dialog = Dialog(source is null ? "Add group" : "Edit group", new ScrollViewer { Content = fields }, "Save");
        var error = Label("");
        fields.Children.Add(error);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name.Text))
                    throw new ArgumentException("Enter a group name.");
                var canonical = Validation.GroupUrl(url.Text.Trim());
                if (model.Groups.Any(group => group.Url == canonical && group.Id != source?.Id))
                    throw new ArgumentException("This group already exists.");
            }
            catch (ArgumentException ex)
            {
                error.Text = ex.Message;
                args.Cancel = true;
            }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        await storage.SaveGroupAsync(new SocialGroup
        {
            Id = source?.Id ?? 0, Name = name.Text.Trim(), Url = url.Text.Trim(), Notes = notes.Text.Trim(),
            Rules = rules.Text.Trim(), Enabled = enabled.IsChecked == true
        }, category.Text.Trim());
        await model.ReloadAsync();
        ShowGroups();
    }
}

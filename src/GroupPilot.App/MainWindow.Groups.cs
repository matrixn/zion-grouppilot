using GroupPilot.Core;
using Microsoft.UI.Xaml.Controls;

namespace GroupPilot_App;

public sealed partial class MainWindow
{
    private void ShowGroups()
    {
        var page = Page("Groups", "Manage Facebook group URLs, categories and posting rules.");
        var search = Input("Search groups");
        var categories = Choices("Category", new[] { "All categories" }.Concat(
            model.Groups.Select(group => group.Category?.Name).OfType<string>().Distinct().Order()), "All categories");
        var groups = List(model.Groups);
        var empty = Label("");
        void Filter()
        {
            var matches = model.Groups.Where(group =>
                (categories.SelectedIndex <= 0 || Equals(group.Category?.Name, categories.SelectedItem)) &&
                (group.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) ||
                 group.Url.Contains(search.Text, StringComparison.OrdinalIgnoreCase))).ToArray();
            groups.ItemsSource = matches;
            empty.Text = matches.Length == 0 ? "No groups match. Add a group or import a CSV." : "";
        }

        page.Children.Add(search);
        page.Children.Add(categories);
        page.Children.Add(empty);
        page.Children.Add(groups);
        search.TextChanged += (_, _) => Filter();
        categories.SelectionChanged += (_, _) => Filter();
        Filter();

        SocialGroup Selected() => groups.SelectedItem as SocialGroup ?? throw new ArgumentException("Select a group first.");
        Action(page, "Add group", () => EditGroupAsync(null));
        Action(page, "Edit selected group", () => EditGroupAsync(Selected()));
        Action(page, "Open selected group", () => OpenFacebookAsync(Selected().Url));
        Action(page, "Delete selected group", async () =>
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
        var importResult = Label("CSV columns: name,url. A URL-only file also works.");
        page.Children.Add(importResult);
        Action(page, "Import CSV", async () =>
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
        var fields = new StackPanel { Spacing = 8 };
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

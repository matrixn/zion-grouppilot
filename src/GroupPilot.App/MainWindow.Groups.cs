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
        var ranges = Choices("Member range", GroupMemberRanges(), MemberRange.All);
        var order = Choices("Sort groups", new[] { "Name", "Members: low to high", "Members: high to low" }, "Name");
        var empty = Label("");
        var cardHost = new StackPanel();
        void Filter()
        {
            var range = ranges.SelectedItem as MemberRange ?? MemberRange.All;
            var matches = model.Groups.Where(group =>
                range.Includes(group.MemberCount) &&
                (visibility.SelectedIndex <= 0 || Equals(visibility.SelectedItem,
                    group.FacebookVisibility.Length == 0 ? "Unknown" : group.FacebookVisibility)) &&
                (categories.SelectedIndex <= 0 || Equals(group.Category?.Name, categories.SelectedItem)) &&
                (group.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) ||
                 group.Url.Contains(search.Text, StringComparison.OrdinalIgnoreCase)));
            var sorted = order.SelectedIndex switch
            {
                1 => matches.OrderBy(group => group.MemberCount is null).ThenBy(group => group.MemberCount),
                2 => matches.OrderBy(group => group.MemberCount is null).ThenByDescending(group => group.MemberCount),
                _ => matches.OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
            };
            var groups = sorted.ThenBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            cardHost.Children.Clear();
            empty.Text = groups.Length == 0 ? "No groups match." : $"{groups.Length:N0} groups";
            if (groups.Length == 0)
                return;

            var cards = ResponsiveGrid(cardHost, 3, 252);
            foreach (var group in groups)
            {
                var card = Section(cards, group.Name);
                card.Children.Add(CreateThumbnail(group.FacebookImageUrl, 132));
                card.Children.Add(Label(group.MemberCount is { } count ? $"~{count:N0} members" : "Member count unknown"));
                card.Children.Add(Label(string.Join(" | ", new[]
                {
                    group.Category?.Name, group.FacebookVisibility, group.PostingAccess.ToString()
                }.Where(value => !string.IsNullOrWhiteSpace(value)))));
                if (!string.IsNullOrWhiteSpace(group.FacebookActivity))
                    card.Children.Add(Label(group.FacebookActivity));
                if (group.FacebookScannedAt is { } scanned && group.FacebookCardText.Length > 0)
                {
                    var details = new Expander { Header = $"Facebook scan: {scanned.ToLocalTime():g}", Content = Label(group.FacebookCardText) };
                    card.Children.Add(details);
                }
                var actions = ResponsiveGrid(card, 2, 105);
                Action(actions, "Edit", () => EditGroupAsync(group));
                Action(actions, "Review in Facebook", () => OpenFacebookAsync(group.Url, group: group));
                Action(actions, "Delete", async () =>
                {
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
            }
        }

        ResponsiveGrid(directory, 5, 168, search, categories, visibility, ranges, order);
        Action(directory, "Add group", () => EditGroupAsync(null), primary: true);
        directory.Children.Add(empty);
        page.Children.Add(cardHost);
        search.TextChanged += (_, _) => Filter();
        categories.SelectionChanged += (_, _) => Filter();
        visibility.SelectionChanged += (_, _) => Filter();
        ranges.SelectionChanged += (_, _) => Filter();
        order.SelectionChanged += (_, _) => Filter();
        Filter();

        var sync = Section(page, "Facebook sync");
        sync.Children.Add(Label("Open your groups and choose Sync groups. GroupPilot scrolls the list and previews additions and removals."));
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
            var previousRange = ranges.SelectedItem as MemberRange;
            var updatedRanges = GroupMemberRanges();
            ranges.ItemsSource = updatedRanges;
            ranges.SelectedItem = updatedRanges.FirstOrDefault(range => range == previousRange) ?? MemberRange.All;
            importResult.Text = $"Imported {result.Added}; skipped {result.Skipped} invalid or duplicate rows.";
        });
    }

    private IReadOnlyList<MemberRange> GroupMemberRanges()
    {
        var counts = model.Groups.Where(group => group.MemberCount.HasValue)
            .Select(group => group.MemberCount!.Value).Distinct().Order().ToArray();
        var ranges = new List<MemberRange> { MemberRange.All };
        if (model.Groups.Any(group => group.MemberCount is null))
            ranges.Add(new MemberRange("Unknown", null, null));
        if (counts.Length == 0)
            return ranges;

        var bucketCount = Math.Min(5, counts.Length);
        for (var index = 0; index < bucketCount; index++)
        {
            var min = counts[index * counts.Length / bucketCount];
            var max = counts[(index + 1) * counts.Length / bucketCount - 1];
            ranges.Add(new MemberRange(min == max ? $"{min:N0}" : $"{min:N0} - {max:N0}", min, max));
        }
        return ranges;
    }

    private sealed record MemberRange(string Label, long? Min, long? Max)
    {
        public static readonly MemberRange All = new("All member counts", null, null);
        public bool Includes(long? count) => this == All || (Min is null ? count is null : count >= Min && count <= Max);
        public override string ToString() => Label;
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

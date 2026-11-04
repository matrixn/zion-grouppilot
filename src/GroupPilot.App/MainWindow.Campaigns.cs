using GroupPilot.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.ApplicationModel.DataTransfer;

namespace GroupPilot_App;

public sealed partial class MainWindow
{
    private ListView CreateCampaignPostPicker(Post[] library)
    {
        var items = new List<ListViewItem>(library.Length);
        foreach (var post in library)
        {
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(CreatePostThumbnail(post, 86));
            var details = new StackPanel { Spacing = 4 };
            var title = Label(post.Title);
            title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            details.Children.Add(title);
            var excerpt = Label(string.IsNullOrWhiteSpace(post.Body) ? "No post text available." : post.Body);
            excerpt.MaxLines = 3;
            excerpt.TextTrimming = TextTrimming.CharacterEllipsis;
            details.Children.Add(excerpt);
            var link = Label(post.Url);
            link.MaxLines = 1;
            link.TextTrimming = TextTrimming.CharacterEllipsis;
            details.Children.Add(link);
            Grid.SetColumn(details, 1);
            row.Children.Add(details);
            items.Add(new ListViewItem { Content = row, Tag = post, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }

        var picker = List(items);
        picker.SelectedIndex = 0;
        return picker;
    }

    private void ShowCampaigns()
    {
        var page = Page("Campaigns", "Run a campaign now or save it for later. Confirm each share in Facebook.");
        var saved = Section(page, "Saved campaigns");
        var campaigns = List(model.Campaigns);
        saved.Children.Add(model.Campaigns.Count == 0 ? Label("No campaigns yet.") : campaigns);
        var actions = ResponsiveGrid(saved, 3, 182);
        Action(actions, "Create campaign", CreateCampaignAsync, primary: true);
        Action(actions, "Run campaign now", () =>
        {
            var selected = campaigns.SelectedItem as Campaign ?? throw new ArgumentException("Select a campaign.");
            ShowCampaignDetails(selected);
            return RunCampaignNowAsync(selected);
        });
        Action(actions, "Open selected campaign", () =>
        {
            ShowCampaignDetails(campaigns.SelectedItem as Campaign ?? throw new ArgumentException("Select a campaign."));
            return Task.CompletedTask;
        });
    }

    private async Task CreateCampaignAsync()
    {
        var groups = model.Groups.Where(group => group.Enabled).ToArray();
        var library = model.Posts.Where(IsLibraryPost).ToArray();
        if (library.Length == 0 || groups.Length == 0)
            throw new ArgumentException("Import a Facebook post and enable at least one group first.");

        var fields = new StackPanel { Spacing = 10 };
        var name = Input("Campaign name");
        var post = CreateCampaignPostPicker(library);
        var targets = List(groups, ListViewSelectionMode.Multiple);
        var date = new CalendarDatePicker { Header = "Planned date (optional)" };
        var time = new TimePicker { Header = "Planned time (local)", SelectedTime = new TimeSpan(9, 0, 0) };
        var error = Label("");
        foreach (var control in new UIElement[] { name, Label("Select a Facebook post"), post, Label("Target groups"), targets })
            fields.Children.Add(control);
        ResponsiveGrid(fields, 2, 160, date, time);
        fields.Children.Add(error);

        var dialog = Dialog("Create campaign", new ScrollViewer { Content = fields }, "Save");
        dialog.SecondaryButtonText = "Save and run now";
        DateTimeOffset? scheduled = null;
        bool Validate()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name.Text) || targets.SelectedItems.Count == 0 || post.SelectedItem is null)
                    throw new ArgumentException("Enter a name, select a post and select at least one group.");
                if (date.Date is { } selected)
                {
                    var local = DateTime.SpecifyKind(selected.Date + time.SelectedTime.GetValueOrDefault(), DateTimeKind.Unspecified);
                    if (TimeZoneInfo.Local.IsInvalidTime(local))
                        throw new ArgumentException("That local time does not exist. Choose a different time.");
                    scheduled = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
                }
                return true;
            }
            catch (ArgumentException ex)
            {
                error.Text = ex.Message;
                return false;
            }
        }

        dialog.PrimaryButtonClick += (_, args) => args.Cancel = !Validate();
        dialog.SecondaryButtonClick += (_, args) => args.Cancel = !Validate();
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None || post.SelectedItem is not ListViewItem { Tag: Post selected })
            return;

        var campaignId = await storage.SaveCampaignAsync(name.Text.Trim(), selected.Id,
            targets.SelectedItems.Cast<SocialGroup>().Select(group => group.Id).ToArray(), scheduled);
        await model.ReloadAsync();
        var campaign = model.Campaigns.Single(item => item.Id == campaignId);
        ShowCampaignDetails(campaign);
        if (result == ContentDialogResult.Secondary)
            await RunCampaignNowAsync(campaign);
    }

    private void ShowCampaignDetails(Campaign campaign)
    {
        var post = model.Posts.Single(item => item.Id == campaign.PostId);
        var page = Page(campaign.Name, $"{post.Title} · {campaign.Targets.Count} targets");
        if (campaign.ScheduledFor is { } scheduled)
            page.Children.Add(Label($"Planned for {scheduled.ToLocalTime():f}"));
        var targets = List(campaign.Targets.OrderBy(target => target.Id));
        targets.SelectedItem = campaign.Targets.FirstOrDefault(target => target.Status == TargetStatus.InProgress);
        var targetSection = Section(page, "Publishing targets");
        targetSection.Children.Add(targets);
        var actions = ResponsiveGrid(targetSection, 3, 180);
        CampaignGroup Selected() => targets.SelectedItem as CampaignGroup ?? throw new ArgumentException("Select a target.");

        if (CanSharePost(post))
        {
            Action(actions, "Run campaign now", () => RunCampaignNowAsync(campaign), primary: true);
            Action(actions, "Prepare selected target", () => AssistAsync(campaign.Id, post, Selected()));
        }
        else
        {
            targetSection.Children.Add(Label("This campaign has no existing Facebook post to share. You can still record its outcomes."));
        }
        Action(actions, "Record selected outcome", () => RecordOutcomeAsync(campaign, Selected()));
        Action(page, "Back to campaigns", () =>
        {
            ShowCampaigns();
            return Task.CompletedTask;
        });
    }

    private CampaignGroup? NextCampaignTarget(Campaign campaign, int? excludeTarget = null) => campaign.Targets
        .Where(target => target.Id != excludeTarget && CanPublish(target))
        .OrderBy(target => target.Status == TargetStatus.InProgress ? 0 : target.Status == TargetStatus.Pending ? 1 : 2)
        .ThenBy(target => target.Id).FirstOrDefault();

    private Task RunCampaignNowAsync(Campaign campaign, int? excludeTarget = null)
    {
        var post = model.Posts.Single(item => item.Id == campaign.PostId);
        if (!CanSharePost(post))
            throw new ArgumentException("This campaign has no existing Facebook post to share.");

        var next = NextCampaignTarget(campaign, excludeTarget)
            ?? throw new ArgumentException("No eligible groups are ready. Check group access or record outcomes awaiting approval.");
        return AssistAsync(campaign.Id, post, next);
    }

    private static bool CanSharePost(Post post) => post.Kind == PostKind.Existing && Validation.IsFacebookPostUrl(post.Url);

    private bool CanPublish(CampaignGroup target) =>
        target.Status is TargetStatus.Pending or TargetStatus.Failed or TargetStatus.InProgress &&
        model.Groups.Any(group => group.Id == target.SocialGroupId && group.Enabled
            && group.Url == target.GroupUrl && group.PostingAccess != GroupPostingAccess.Unavailable);

    private async Task AssistAsync(int campaignId, Post post, CampaignGroup target)
    {
        if (!CanPublish(target))
            throw new ArgumentException("This target is closed, awaiting approval, or its group is unavailable or changed.");
        var group = model.Groups.Single(item => item.Id == target.SocialGroupId);
        var panel = new StackPanel { Spacing = 8 };
        if (target.Status == TargetStatus.InProgress)
        {
            panel.Children.Add(Label("This target was left in progress. Check Facebook for an existing submission before posting again. If it was already submitted, record its outcome."));
        }
        panel.Children.Add(Label("Check your account, the group rules and permission to post in Facebook. Paste the copied Facebook post link and share it manually."));
        panel.Children.Add(Label(group.ReviewedAt is { } reviewed
            ? $"Last manual review: {group.PostingAccess} · {reviewed.ToLocalTime():g}"
            : "Posting access has not been reviewed."));
        if (group.Rules.Length > 0)
            panel.Children.Add(Label($"Group rules: {group.Rules}"));
        if (await Dialog($"Prepare for {target.GroupName}", new ScrollViewer { Content = panel, MaxHeight = 400 }, "Copy and open").ShowAsync()
            != ContentDialogResult.Primary)
            return;

        var data = new DataPackage();
        data.SetText(post.Url);
        Clipboard.SetContent(data);
        await storage.BeginTargetAsync(target.Id);
        await model.ReloadAsync();
        ShowCampaignDetails(model.Campaigns.Single(item => item.Id == campaignId));
        await OpenFacebookAsync(target.GroupUrl, group, (campaignId, target.Id));
    }

    private async Task RecordOutcomeAsync(Campaign campaign, CampaignGroup target, string suggestedUrl = "")
    {
        if (target.Status is TargetStatus.Published or TargetStatus.Skipped)
            throw new ArgumentException("This target is already closed.");
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Label("Choose Published only after confirming the post in Facebook."));
        var outcome = Choices("Outcome", new[]
        {
            TargetStatus.Published, TargetStatus.AwaitingApproval, TargetStatus.Failed, TargetStatus.Skipped
        }, TargetStatus.Pending);
        var url = Input("Published Facebook URL", suggestedUrl);
        var error = Label("");
        panel.Children.Add(outcome);
        panel.Children.Add(url);
        panel.Children.Add(error);
        var dialog = Dialog("Record publishing outcome", panel, "Record");
        if (CanSharePost(model.Posts.Single(post => post.Id == campaign.PostId)) && NextCampaignTarget(campaign, target.Id) is not null)
            dialog.SecondaryButtonText = "Record and open next";
        bool Validate()
        {
            if (outcome.SelectedItem is null)
                error.Text = "Choose the outcome you observed in Facebook.";
            else if (Equals(outcome.SelectedItem, TargetStatus.Published) && !Validation.IsFacebookPostUrl(url.Text.Trim()))
                error.Text = "Enter the HTTPS Facebook URL of the published post.";
            else
                error.Text = "";
            return error.Text.Length == 0;
        }

        dialog.PrimaryButtonClick += (_, args) => args.Cancel = !Validate();
        dialog.SecondaryButtonClick += (_, args) => args.Cancel = !Validate();
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None)
            return;
        await storage.RecordAttemptAsync(target.Id, (TargetStatus)outcome.SelectedItem, url.Text.Trim());
        if (publishingTarget?.TargetId == target.Id)
        {
            publishingTarget = null;
            if (outcomeButton is not null)
            {
                outcomeButton.IsEnabled = false;
                outcomeButton.Visibility = Visibility.Collapsed;
            }
            if (browserContext is not null)
                browserContext.Text = "Campaign outcome recorded.";
        }
        await model.ReloadAsync();
        var refreshed = model.Campaigns.Single(item => item.Id == campaign.Id);
        ShowCampaignDetails(refreshed);
        if (refreshed.Targets.All(item => item.Status is TargetStatus.Published or TargetStatus.Skipped))
            NotifyCampaignClosed();
        else if (result == ContentDialogResult.Secondary && NextCampaignTarget(refreshed, target.Id) is not null)
            await RunCampaignNowAsync(refreshed, target.Id);
    }

    private static void NotifyCampaignClosed()
    {
        try
        {
            if (AppNotificationManager.IsSupported())
                AppNotificationManager.Default.Show(new AppNotificationBuilder()
                    .SetAppLogoOverride(new Uri("ms-appx:///Assets/Brand.png"), AppNotificationImageCrop.Default)
                    .AddText("GroupPilot").AddText("All targets in this campaign are closed.").BuildNotification());
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Windows notification unavailable");
        }
    }
}

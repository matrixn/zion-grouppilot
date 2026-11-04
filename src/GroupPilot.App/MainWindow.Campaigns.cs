using GroupPilot.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.ApplicationModel.DataTransfer;

namespace GroupPilot_App;

public sealed partial class MainWindow
{
    private void ShowCampaigns()
    {
        var page = Page("Campaigns", "Save a plan and resume pending or failed targets. Schedule times are local planning metadata.");
        var saved = Section(page, "Saved campaigns");
        var campaigns = List(model.Campaigns);
        saved.Children.Add(model.Campaigns.Count == 0 ? Label("No campaigns yet.") : campaigns);
        var actions = ResponsiveGrid(saved, 2, 182);
        Action(actions, "Create campaign", CreateCampaignAsync, primary: true);
        Action(actions, "Open selected campaign", () =>
        {
            ShowCampaignDetails(campaigns.SelectedItem as Campaign ?? throw new ArgumentException("Select a campaign."));
            return Task.CompletedTask;
        });
    }

    private async Task CreateCampaignAsync()
    {
        var groups = model.Groups.Where(group => group.Enabled).ToArray();
        var library = model.Posts.Where(item => !item.IsArchived).ToArray();
        if (library.Length == 0 || groups.Length == 0)
            throw new ArgumentException("Save a post and enable at least one group first.");

        var fields = new StackPanel { Spacing = 10 };
        var name = Input("Campaign name");
        var post = Choices("Saved post", library, library[0]);
        var targets = List(groups, ListViewSelectionMode.Multiple);
        var date = new CalendarDatePicker { Header = "Planned date (optional)" };
        var time = new TimePicker { Header = "Planned time (local)", SelectedTime = new TimeSpan(9, 0, 0) };
        var error = Label("");
        foreach (var control in new UIElement[] { name, post, Label("Target groups"), targets })
            fields.Children.Add(control);
        ResponsiveGrid(fields, 2, 160, date, time);
        fields.Children.Add(error);

        var dialog = Dialog("Create campaign", new ScrollViewer { Content = fields }, "Save");
        DateTimeOffset? scheduled = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name.Text) || targets.SelectedItems.Count == 0)
                    throw new ArgumentException("Enter a name and select at least one group.");
                if (date.Date is { } selected)
                {
                    var local = DateTime.SpecifyKind(selected.Date + time.SelectedTime.GetValueOrDefault(), DateTimeKind.Unspecified);
                    if (TimeZoneInfo.Local.IsInvalidTime(local))
                        throw new ArgumentException("That local time does not exist. Choose a different time.");
                    scheduled = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
                }
            }
            catch (ArgumentException ex)
            {
                error.Text = ex.Message;
                args.Cancel = true;
            }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        await storage.SaveCampaignAsync(name.Text.Trim(), ((Post)post.SelectedItem).Id,
            targets.SelectedItems.Cast<SocialGroup>().Select(group => group.Id).ToArray(), scheduled);
        await model.ReloadAsync();
        ShowCampaigns();
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

        Action(actions, "Resume next target", async () =>
        {
            var next = campaign.Targets.Where(CanPublish)
                .OrderByDescending(target => target.Status == TargetStatus.InProgress).ThenBy(target => target.Id)
                .FirstOrDefault() ?? throw new ArgumentException("No actionable targets. Record outcomes for targets awaiting approval.");
            await AssistAsync(campaign.Id, post, next);
        }, primary: true);
        Action(actions, "Prepare selected target", () => AssistAsync(campaign.Id, post, Selected()));
        Action(actions, "Record selected outcome", () => RecordOutcomeAsync(campaign, Selected()));
        Action(page, "Back to campaigns", () =>
        {
            ShowCampaigns();
            return Task.CompletedTask;
        });
    }

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
        panel.Children.Add(Label("Check your account, the group rules and permission to post in Facebook. Paste the copied draft or link and submit manually."));
        panel.Children.Add(Label(group.ReviewedAt is { } reviewed
            ? $"Last manual review: {group.PostingAccess} · {reviewed.ToLocalTime():g}"
            : "Posting access has not been reviewed."));
        if (group.Rules.Length > 0)
            panel.Children.Add(Label($"Group rules: {group.Rules}"));
        if (post.Media.Count > 0)
            panel.Children.Add(Label("Local images to attach:\n" + string.Join("\n", post.Media.Select(media => media.Path))));
        var variant = Choices("Text to copy", new[] { "Main draft" }.Concat(post.Variants.Select(item => item.Body)), "Main draft");
        if (post.Kind != PostKind.Existing)
            panel.Children.Add(variant);
        if (await Dialog($"Prepare for {target.GroupName}", new ScrollViewer { Content = panel, MaxHeight = 400 }, "Copy and open").ShowAsync()
            != ContentDialogResult.Primary)
            return;

        var text = post.Kind == PostKind.Existing ? post.Url :
            string.Join(Environment.NewLine, new[] { variant.SelectedIndex > 0 ? (string)variant.SelectedItem : post.Body, post.Url }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        if (text.Length > 0)
        {
            var data = new DataPackage();
            data.SetText(text);
            Clipboard.SetContent(data);
        }
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
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (outcome.SelectedItem is null)
                error.Text = "Choose the outcome you observed in Facebook.";
            else if (Equals(outcome.SelectedItem, TargetStatus.Published) && !Validation.IsFacebookPostUrl(url.Text.Trim()))
                error.Text = "Enter the HTTPS Facebook URL of the published post.";
            else
                error.Text = "";
            args.Cancel = error.Text.Length > 0;
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        await storage.RecordAttemptAsync(target.Id, (TargetStatus)outcome.SelectedItem, url.Text.Trim());
        if (publishingTarget?.TargetId == target.Id)
        {
            publishingTarget = null;
            if (outcomeButton is not null)
                outcomeButton.IsEnabled = false;
            if (browserContext is not null)
                browserContext.Text = "Campaign outcome recorded.";
        }
        await model.ReloadAsync();
        var refreshed = model.Campaigns.Single(item => item.Id == campaign.Id);
        ShowCampaignDetails(refreshed);
        if (refreshed.Targets.All(item => item.Status is TargetStatus.Published or TargetStatus.Skipped))
            NotifyCampaignClosed();
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

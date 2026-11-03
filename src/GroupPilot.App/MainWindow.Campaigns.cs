using GroupPilot.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.ApplicationModel.DataTransfer;

namespace GroupPilot_App;

public sealed partial class MainWindow
{
    private Campaign? activeCampaign;

    private void ShowCampaigns()
    {
        var page = Page("Campaigns", "Save a plan and resume pending or failed targets. Schedule times are local planning metadata.");
        var campaigns = List(model.Campaigns);
        page.Children.Add(model.Campaigns.Count == 0 ? Label("No campaigns yet.") : campaigns);
        Action(page, "Create campaign", CreateCampaignAsync);
        Action(page, "Open selected campaign", () =>
        {
            ShowCampaignDetails(campaigns.SelectedItem as Campaign ?? throw new ArgumentException("Select a campaign."));
            return Task.CompletedTask;
        });
    }

    private async Task CreateCampaignAsync()
    {
        var groups = model.Groups.Where(group => group.Enabled).ToArray();
        if (model.Posts.Count == 0 || groups.Length == 0)
            throw new ArgumentException("Save a post and enable at least one group first.");

        var fields = new StackPanel { Spacing = 10 };
        var name = Input("Campaign name");
        var post = Choices("Saved post", model.Posts, model.Posts[0]);
        var targets = List(groups, ListViewSelectionMode.Multiple);
        var date = new CalendarDatePicker { Header = "Planned date (optional)" };
        var time = new TimePicker { Header = "Planned time (local)", SelectedTime = new TimeSpan(9, 0, 0) };
        var error = Label("");
        foreach (var control in new UIElement[] { name, post, Label("Target groups"), targets, date, time, error })
            fields.Children.Add(control);

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
        activeCampaign = campaign;
        var post = model.Posts.Single(item => item.Id == campaign.PostId);
        var page = Page(campaign.Name, $"{post.Title} · {campaign.Targets.Count} targets");
        if (campaign.ScheduledFor is { } scheduled)
            page.Children.Add(Label($"Planned for {scheduled.ToLocalTime():f}"));
        var targets = List(campaign.Targets);
        page.Children.Add(targets);
        CampaignGroup Selected() => targets.SelectedItem as CampaignGroup ?? throw new ArgumentException("Select a target.");

        Action(page, "Resume next target", async () =>
        {
            var next = campaign.Targets.FirstOrDefault(CanPublish)
                ?? throw new ArgumentException("All targets are closed or unavailable.");
            await AssistAsync(post, next);
        });
        Action(page, "Prepare selected target", () => AssistAsync(post, Selected()));
        Action(page, "Record selected outcome", () => RecordOutcomeAsync(campaign, Selected()));
        Action(page, "Back to campaigns", () =>
        {
            activeCampaign = null;
            ShowCampaigns();
            return Task.CompletedTask;
        });
    }

    private bool CanPublish(CampaignGroup target) => target.Status is TargetStatus.Pending or TargetStatus.Failed
        && model.Groups.Any(group => group.Id == target.SocialGroupId && group.Enabled);

    private async Task AssistAsync(Post post, CampaignGroup target)
    {
        if (!CanPublish(target))
            throw new ArgumentException("This target is closed or its group is unavailable.");
        var group = model.Groups.Single(item => item.Id == target.SocialGroupId);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Label("GroupPilot copies the draft text and opens the group. Paste the draft, attach images and confirm the submission in Facebook."));
        if (group.Rules.Length > 0)
            panel.Children.Add(Label($"Group rules: {group.Rules}"));
        if (post.Media.Count > 0)
            panel.Children.Add(Label("Local images to attach:\n" + string.Join("\n", post.Media.Select(media => media.Path))));
        var variant = Choices("Text to copy", new[] { "Main draft" }.Concat(post.Variants.Select(item => item.Body)), "Main draft");
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
        await OpenFacebookAsync(target.GroupUrl);
    }

    private async Task RecordOutcomeAsync(Campaign campaign, CampaignGroup target)
    {
        if (target.Status is TargetStatus.Published or TargetStatus.Skipped)
            throw new ArgumentException("This target is already closed.");
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Label("Choose Published only after confirming the post in Facebook."));
        var outcome = Choices("Outcome", new[] { TargetStatus.Published, TargetStatus.Failed, TargetStatus.Skipped }, TargetStatus.Published);
        var url = Input("Published Facebook URL");
        var error = Label("");
        panel.Children.Add(outcome);
        panel.Children.Add(url);
        panel.Children.Add(error);
        var dialog = Dialog("Record publishing outcome", panel, "Record");
        dialog.PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = Equals(outcome.SelectedItem, TargetStatus.Published) && !Validation.IsFacebookPostUrl(url.Text.Trim());
            error.Text = args.Cancel ? "Enter the HTTPS Facebook URL of the published post." : "";
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        await storage.RecordAttemptAsync(target.Id, (TargetStatus)outcome.SelectedItem, url.Text.Trim());
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
                    .AddText("GroupPilot").AddText("All targets in this campaign are closed.").BuildNotification());
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Windows notification unavailable");
        }
    }
}

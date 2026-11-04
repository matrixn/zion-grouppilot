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
        var active = model.Campaigns.Where(campaign => !campaign.IsArchived).ToArray();
        var campaignItems = active.Select(campaign =>
        {
            var row = new Grid { ColumnSpacing = 14, Padding = new Thickness(8) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Spacing = 5 };
            var name = Label(campaign.Name);
            name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            text.Children.Add(name);
            text.Children.Add(Label($"{CampaignSchedule.Label(campaign.Recurrence)} · Run {campaign.RunNumber}" +
                (campaign.NextRunAt is { } next ? $" · Next {next.ToLocalTime():g}" : "")));
            row.Children.Add(text);
            var groups = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            groups.Children.Add(Label($"{campaign.Targets.Count} groups"));
            var info = new Border
            {
                Padding = new Thickness(6), Child = new FontIcon { Glyph = "\uE946", FontSize = 18 }
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(info, "Campaign groups");
            var names = new StackPanel { Spacing = 5 };
            foreach (var target in campaign.Targets.OrderBy(target => target.GroupName))
                names.Children.Add(Label(target.GroupName));
            ToolTipService.SetToolTip(info, new ToolTip
            {
                Content = new ScrollViewer { Content = names, MaxHeight = 280, MaxWidth = 380 }
            });
            groups.Children.Add(info);
            Grid.SetColumn(groups, 1);
            row.Children.Add(groups);
            return new ListViewItem { Content = row, Tag = campaign, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        }).ToArray();
        var campaigns = List(campaignItems);
        saved.Children.Add(active.Length == 0 ? Label("No campaigns yet.") : campaigns);
        var actions = ResponsiveGrid(saved, 4, 172);
        Action(actions, "Create campaign", CreateCampaignAsync, primary: true);
        Action(actions, "Run campaign now", () =>
        {
            var selected = (campaigns.SelectedItem as ListViewItem)?.Tag as Campaign ?? throw new ArgumentException("Select a campaign.");
            ShowCampaignDetails(selected);
            return RunCampaignNowAsync(selected);
        });
        Action(actions, "Delete selected campaign", () => DeleteCampaignAsync(
            (campaigns.SelectedItem as ListViewItem)?.Tag as Campaign ?? throw new ArgumentException("Select a campaign.")));
        Action(actions, "Open selected campaign", () =>
        {
            ShowCampaignDetails((campaigns.SelectedItem as ListViewItem)?.Tag as Campaign ?? throw new ArgumentException("Select a campaign."));
            return Task.CompletedTask;
        });
    }

    private Task CreateCampaignAsync()
    {
        var groups = model.Groups.Where(group => group.Enabled).ToArray();
        var library = model.Posts.Where(IsLibraryPost).ToArray();
        if (library.Length == 0 || groups.Length == 0)
            throw new ArgumentException("Import a Facebook post and enable at least one group first.");

        var page = Page("New campaign", "Choose a post, select groups and set the sharing schedule.");
        var overview = Label("1 Campaign   →   2 Facebook post   →   3 Target groups   →   4 Schedule");
        overview.Style = ResourceStyle("WorkspaceDescriptionStyle");
        page.Children.Add(overview);
        var introduction = Section(page, "1 · Campaign");
        var name = Input("Campaign name");
        name.PlaceholderText = "For example, ZionFood weekly promotion";
        introduction.Children.Add(name);

        var postSection = Section(page, "2 · Facebook post");
        var post = CreateCampaignPostPicker(library);
        post.MaxHeight = 240;
        postSection.Children.Add(post);
        var selection = Label("");
        postSection.Children.Add(selection);
        void UpdatePost() => selection.Text = post.SelectedItem is ListViewItem { Tag: Post chosen }
            ? $"Selected: {chosen.Title}" : "Select one post.";
        post.SelectionChanged += (_, _) => UpdatePost();
        UpdatePost();

        var groupSection = Section(page, "3 · Target groups");
        groupSection.Children.Add(Label("Select the groups that will receive this post."));
        var search = Input("Find a group");
        search.PlaceholderText = "Search by name";
        groupSection.Children.Add(search);
        var targets = List(groups, ListViewSelectionMode.Multiple);
        targets.MaxHeight = 280;
        var groupCount = Label($"0 of {groups.Length} groups selected");
        groupSection.Children.Add(groupCount);
        groupSection.Children.Add(targets);
        var selectedIds = new HashSet<int>();
        var groupActions = ResponsiveGrid(groupSection, 2, 150);
        Action(groupActions, "Select visible groups", () =>
        {
            targets.SelectAll();
            return Task.CompletedTask;
        });
        Action(groupActions, "Clear selection", () =>
        {
            selectedIds.Clear();
            targets.SelectedItems.Clear();
            groupCount.Text = $"0 of {groups.Length} groups selected";
            return Task.CompletedTask;
        });
        var filtering = false;
        targets.SelectionChanged += (_, args) =>
        {
            if (filtering)
                return;
            foreach (var removed in args.RemovedItems.Cast<SocialGroup>())
                selectedIds.Remove(removed.Id);
            foreach (var added in args.AddedItems.Cast<SocialGroup>())
                selectedIds.Add(added.Id);
            groupCount.Text = $"{selectedIds.Count} of {groups.Length} groups selected";
        };
        search.TextChanged += (_, _) =>
        {
            filtering = true;
            targets.ItemsSource = groups.Where(group => group.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var group in ((SocialGroup[])targets.ItemsSource).Where(group => selectedIds.Contains(group.Id)))
                targets.SelectedItems.Add(group);
            filtering = false;
        };

        var scheduling = Section(page, "4 · Schedule and pause");
        var recurrence = Choices("Repeat campaign", Enum.GetValues<CampaignRecurrence>()
            .Select(value => new RecurrenceChoice(value)).ToArray(), new RecurrenceChoice(CampaignRecurrence.Once));
        var date = new CalendarDatePicker
        {
            Header = "First date", PlaceholderText = "Choose a start date",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var time = new TimePicker
        {
            Header = "Time", SelectedTime = DateTime.Now.TimeOfDay, ClockIdentifier = "24HourClock",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var pause = CampaignPauseInput(120);
        scheduling.Children.Add(recurrence);
        ResponsiveGrid(scheduling, 3, 180, date, time, pause);
        var explanation = Label("");
        scheduling.Children.Add(explanation);
        void UpdateSchedule()
        {
            var value = (recurrence.SelectedItem as RecurrenceChoice)?.Value ?? CampaignRecurrence.Once;
            explanation.Text = value switch
            {
                CampaignRecurrence.Once => "Choose a start date or use Save and run now. The pause is counted after a share, before opening a different group.",
                CampaignRecurrence.Monthly => "Repeats on the selected day each month. Shorter months use their last day.",
                CampaignRecurrence.TwiceMonthly or CampaignRecurrence.ThreeTimesMonthly or CampaignRecurrence.FourTimesMonthly =>
                    "Runs are spaced evenly within each calendar month at the selected time. The first run uses your start date.",
                _ => "Repeats from the start date at the selected local time."
            };
            if (value != CampaignRecurrence.Once && date.Date is null)
                date.Date = DateTimeOffset.Now;
        }
        recurrence.SelectionChanged += (_, _) => UpdateSchedule();
        UpdateSchedule();
        scheduling.Children.Add(Label("Keep GroupPilot open for scheduled reminders. Confirm each submission in Facebook."));

        // Actions remain visible while the form scrolls.
        var scroll = (ScrollViewer)ContentHost.Content;
        scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        ContentHost.Content = null;
        var layout = new Grid { RowSpacing = 10 };
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(scroll);
        var footer = new StackPanel { Spacing = 8, Padding = new Thickness(24, 12, 24, 16) };
        footer.Children.Add(Label("Save when ready, or start sharing immediately."));
        var actions = ResponsiveGrid(footer, 3, 160);
        async Task Save(bool runNow)
        {
            if (string.IsNullOrWhiteSpace(name.Text) || selectedIds.Count == 0 || post.SelectedItem is not ListViewItem { Tag: Post chosen })
                throw new ArgumentException("Enter a campaign name, select a post and select at least one group.");
            if (double.IsNaN(pause.Value) || pause.Value < 1 || pause.Value > 86400 || pause.Value != Math.Truncate(pause.Value))
                throw new ArgumentException("Enter a whole number of seconds from 1 to 86400.");
            var repeat = (recurrence.SelectedItem as RecurrenceChoice)?.Value ?? CampaignRecurrence.Once;
            DateTimeOffset? scheduled = null;
            if (date.Date is { } day)
                scheduled = CampaignSchedule.Resolve(day.Date + time.SelectedTime.GetValueOrDefault(), TimeZoneInfo.Local);
            if (repeat != CampaignRecurrence.Once && scheduled is null)
                throw new ArgumentException("Choose a first date for the recurring campaign.");

            var campaignId = await storage.SaveCampaignAsync(name.Text.Trim(), chosen.Id, selectedIds.ToArray(),
                scheduled, (int)pause.Value, repeat, TimeZoneInfo.Local.Id);
            await model.ReloadAsync();
            var campaign = model.Campaigns.Single(item => item.Id == campaignId);
            ShowCampaignDetails(campaign);
            if (runNow)
                await RunCampaignNowAsync(campaign);
        }
        Action(actions, "Save campaign", () => Save(false), primary: true);
        Action(actions, "Save and run now", () => Save(true));
        Action(actions, "Cancel", () => { ShowCampaigns(); return Task.CompletedTask; });
        Grid.SetRow(footer, 1);
        layout.Children.Add(footer);
        ContentHost.Content = layout;
        return Task.CompletedTask;
    }

    private sealed record RecurrenceChoice(CampaignRecurrence Value)
    {
        public override string ToString() => CampaignSchedule.Label(Value);
    }

    private void ShowCampaignDetails(Campaign campaign)
    {
        var post = model.Posts.Single(item => item.Id == campaign.PostId);
        var page = Page(campaign.Name, $"{post.Title} · {campaign.Targets.Count} targets");
        page.Children.Add(Label($"{CampaignSchedule.Label(campaign.Recurrence)} · Run {campaign.RunNumber}" +
            (campaign.NextRunAt is { } next ? $" · Next {next.ToLocalTime():g}" : "")));
        var pacing = Section(page, "Pause between shares");
        var pause = CampaignPauseInput(campaign.PauseSeconds);
        pacing.Children.Add(pause);
        pacing.Children.Add(Label("120 seconds is an application default. It does not guarantee that Facebook will allow a share."));
        Action(pacing, "Save pause", async () =>
        {
            if (double.IsNaN(pause.Value) || pause.Value != Math.Truncate(pause.Value))
                throw new ArgumentException("Enter a whole number of seconds.");
            await storage.UpdateCampaignPauseAsync(campaign.Id, (int)pause.Value);
            await model.ReloadAsync();
            ShowCampaignDetails(model.Campaigns.Single(item => item.Id == campaign.Id));
        }, "Pause saved.");
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
        Action(page, "Delete campaign", () => DeleteCampaignAsync(campaign));
        Action(page, "Back to campaigns", () =>
        {
            ShowCampaigns();
            return Task.CompletedTask;
        });
    }

    private static NumberBox CampaignPauseInput(int seconds) => new()
    {
        Header = "Pause in seconds", Minimum = 1, Maximum = 86400, Value = seconds,
        SmallChange = 10, LargeChange = 60, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
    };

    private async Task<bool> WaitForSharePauseAsync(string groupUrl)
    {
        while (await storage.GetNextShareAtAsync(groupUrl) is { } next && next > DateTimeOffset.UtcNow)
        {
            var message = Label("");
            var dialog = Dialog("Pause before next share", message, "");
            dialog.CloseButtonText = "Stop";
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(250);
            var elapsed = false;
            void Update()
            {
                var remaining = Math.Max(0, (int)Math.Ceiling((next - DateTimeOffset.UtcNow).TotalSeconds));
                message.Text = $"Next group opens in {remaining / 60:00}:{remaining % 60:00}";
                if (remaining == 0)
                {
                    elapsed = true;
                    dialog.Hide();
                }
            }

            timer.Tick += (_, _) => Update();
            message.Text = $"Next group opens at {next.ToLocalTime():T}";
            timer.Start();
            try
            {
                await dialog.ShowAsync();
                if (!elapsed)
                    return false;
            }
            finally
            {
                timer.Stop();
            }
        }
        return true;
    }

    private async Task DeleteCampaignAsync(Campaign campaign)
    {
        if (await Dialog($"Delete {campaign.Name}?", Label("Remove this campaign from the list? Its publishing history is kept."),
            "Delete").ShowAsync() != ContentDialogResult.Primary)
            return;

        await storage.ArchiveCampaignAsync(campaign.Id);
        if (publishingTarget?.CampaignId == campaign.Id)
        {
            publishingTarget = null;
            if (prepareShareButton is not null)
                prepareShareButton.Visibility = Visibility.Collapsed;
            if (outcomeButton is not null)
            {
                outcomeButton.IsEnabled = false;
                outcomeButton.Visibility = Visibility.Collapsed;
            }
            if (browserContext is not null)
                browserContext.Text = "Campaign removed.";
        }
        await model.ReloadAsync();
        ShowCampaigns();
    }

    private CampaignGroup? NextCampaignTarget(Campaign campaign, int? excludeTarget = null) => campaign.Targets
        .Where(target => target.Id != excludeTarget && CanPublish(target))
        .OrderBy(target => target.Status == TargetStatus.InProgress ? 0 : target.Status == TargetStatus.Pending ? 1 : 2)
        .ThenBy(target => target.Id).FirstOrDefault();

    private async Task RunCampaignNowAsync(Campaign campaign, int? excludeTarget = null)
    {
        await storage.PrepareCampaignRunAsync(campaign.Id);
        await model.ReloadAsync();
        campaign = model.Campaigns.Single(item => item.Id == campaign.Id && !item.IsArchived);
        var post = model.Posts.Single(item => item.Id == campaign.PostId);
        if (!CanSharePost(post))
            throw new ArgumentException("This campaign has no existing Facebook post to share.");

        var next = NextCampaignTarget(campaign, excludeTarget)
            ?? throw new ArgumentException("No eligible groups are ready. Check group access or record outcomes awaiting approval.");
        await AssistAsync(campaign.Id, post, next);
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
        if (!await WaitForSharePauseAsync(target.GroupUrl))
            return;
        var group = model.Groups.Single(item => item.Id == target.SocialGroupId);
        var panel = new StackPanel { Spacing = 8 };
        if (target.Status == TargetStatus.InProgress)
        {
            panel.Children.Add(Label("This target was left in progress. Check Facebook for an existing submission before posting again. If it was already submitted, record its outcome."));
        }
        panel.Children.Add(Label("Check your account, the group rules and permission to post in Facebook. GroupPilot will try to insert the selected post link in the group editor. Review it and confirm the submission in Facebook."));
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
        await storage.BeginTargetAsync(target.Id, model.Campaigns.Single(item => item.Id == campaignId).RunNumber);
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
        await storage.RecordAttemptAsync(target.Id, (TargetStatus)outcome.SelectedItem, url.Text.Trim(), campaign.RunNumber);
        if (publishingTarget?.TargetId == target.Id)
        {
            publishingTarget = null;
            if (prepareShareButton is not null)
                prepareShareButton.Visibility = Visibility.Collapsed;
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

using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace GroupPilot_App;

public sealed partial class MainWindow
{
    private DispatcherQueueTimer? campaignReminderTimer;
    private bool checkingReminders;
    private readonly HashSet<(int Id, long DueTicks)> announcedCampaigns = [];

    private void StartCampaignReminders()
    {
        if (campaignReminderTimer is not null)
            return;
        campaignReminderTimer = DispatcherQueue.CreateTimer();
        campaignReminderTimer.Interval = TimeSpan.FromMinutes(1);
        campaignReminderTimer.Tick += async (_, _) => await CheckCampaignRemindersAsync();
        campaignReminderTimer.Start();
        _ = CheckCampaignRemindersAsync();
    }

    private async Task CheckCampaignRemindersAsync()
    {
        if (checkingReminders)
            return;
        checkingReminders = true;
        try
        {
            var scheduled = await storage.QueryAsync(db => db.Campaigns.AsNoTracking()
                .Where(campaign => !campaign.IsArchived && campaign.NextRunAt != null).ToListAsync());
            foreach (var campaign in scheduled.Where(campaign => campaign.NextRunAt <= DateTimeOffset.UtcNow))
            {
                var key = (campaign.Id, campaign.NextRunAt!.Value.UtcTicks);
                if (!announcedCampaigns.Add(key))
                    continue;
                if (AppNotificationManager.IsSupported())
                    AppNotificationManager.Default.Show(new AppNotificationBuilder()
                        .SetAppLogoOverride(new Uri("ms-appx:///Assets/Brand.png"), AppNotificationImageCrop.Default)
                        .AddText("Campaign ready").AddText(campaign.Name + " is due. Open Campaigns to start sharing.")
                        .BuildNotification());
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not check campaign reminders");
        }
        finally
        {
            checkingReminders = false;
        }
    }
}

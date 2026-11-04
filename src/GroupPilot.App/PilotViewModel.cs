using CommunityToolkit.Mvvm.ComponentModel;
using GroupPilot.Core;
using GroupPilot.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace GroupPilot_App;

public sealed partial class PilotViewModel(Storage storage) : ObservableObject
{
    [ObservableProperty] public partial bool Busy { get; private set; }
    [ObservableProperty] public partial string Status { get; private set; } = "";

    public List<Post> Posts { get; private set; } = [];
    public List<SocialGroup> Groups { get; private set; } = [];
    public List<Campaign> Campaigns { get; private set; } = [];
    public List<PublishingAttempt> History { get; private set; } = [];
    public string Theme { get; private set; } = "Default";

    public void SetStatus(string message) => Status = message;

    public async Task ReloadAsync()
    {
        await storage.QueryAsync(async db =>
        {
            Posts = await db.Posts.AsNoTracking().Include(post => post.Media).Include(post => post.Variants)
                .AsSplitQuery().OrderByDescending(post => post.Id).ToListAsync();
            Groups = await db.Groups.AsNoTracking().Include(group => group.Category).OrderBy(group => group.Name).ToListAsync();
            Campaigns = await db.Campaigns.AsNoTracking().Include(campaign => campaign.Targets.Where(target => !target.IsExcluded))
                .OrderByDescending(campaign => campaign.Id).ToListAsync();
            History = await db.Attempts.AsNoTracking().Include(attempt => attempt.Target)
                .OrderByDescending(attempt => attempt.Id).ToListAsync();
            Theme = await db.Settings.Select(settings => settings.Theme).FirstOrDefaultAsync() ?? "Default";
            return true;
        });
    }

    public async Task RunAsync(Func<Task> action, string success = "")
    {
        if (Busy)
            return;

        Busy = true;
        Status = "Working…";
        try
        {
            await action();
            Status = success;
        }
        catch (ArgumentException ex)
        {
            Status = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GroupPilot operation failed");
            Status = "The operation failed. Check the local log and try again.";
        }
        finally
        {
            Busy = false;
        }
    }
}

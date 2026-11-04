using System.Text.Json;
using GroupPilot.Core;
using GroupPilot.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GroupPilot_App;

public sealed partial class MainWindow
{
    private CancellationTokenSource? automaticCampaign;
    private Button? stopCampaignButton;
    private Panel? browserNavigation;

    private async Task RunAutomaticCampaignAsync(Campaign campaign, CancellationTokenSource cancellation)
    {
        var progress = new Progress<CampaignRunProgress>(update =>
        {
            if (automaticCampaign != cancellation)
                return;
            ShowAutomaticStatus($"{campaign.Name} · {update.Completed}/{update.Total} · {update.GroupName}",
                update.Message, InfoBarSeverity.Informational);
        });
        try
        {
            var runner = new CampaignRunner(storage, new CampaignBrowser(this));
            var result = await runner.RunAsync(campaign.Id, campaign.RunNumber, progress, cancellation.Token);
            ShowAutomaticStatus(campaign.Name, result.Message, result.Completed ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            model.SetStatus(result.Message);
        }
        catch (OperationCanceledException)
        {
            ShowAutomaticStatus(campaign.Name, "Campaign stopped. Saved outcomes are kept; uncertain submissions must be reviewed before retrying.", InfoBarSeverity.Warning);
            model.SetStatus("Campaign stopped. Check its saved outcomes before resuming.");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Automatic campaign {CampaignId} stopped", campaign.Id);
            ShowAutomaticStatus(campaign.Name, "Campaign stopped. Check the target and local log before resuming.", InfoBarSeverity.Error);
            model.SetStatus("Campaign stopped. Check the target and local log before resuming.");
        }
        finally
        {
            // Remove the ownership guard before controls are re-enabled. No queued progress may replace the final result.
            automaticCampaign = null;
            cancellation.Dispose();
            if (stopCampaignButton is not null)
                stopCampaignButton.Visibility = Visibility.Collapsed;
            if (browserNavigation is not null)
                browserNavigation.IsHitTestVisible = true;
            if (browserAddress is not null)
                browserAddress.IsEnabled = true;
            if (browser is not null)
                browser.IsHitTestVisible = true;
            if (prepareShareButton is not null)
                prepareShareButton.IsEnabled = true;
            if (outcomeButton is not null)
                outcomeButton.IsEnabled = true;
            try { await model.ReloadAsync(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Could not refresh automatic campaign results"); }
        }
    }

    private void ShowAutomaticStatus(string title, string message, InfoBarSeverity severity)
    {
        if (sharingBanner is null)
            return;
        sharingBanner.Title = title;
        sharingBanner.Message = message;
        sharingBanner.Severity = severity;
        sharingBanner.IsOpen = true;
    }

    private async Task<PublicationResult> PublishInFacebookAsync(CampaignPublication publication, Func<Task> beforeSubmit,
        CancellationToken cancellationToken)
    {
        var group = model.Groups.Single(item => item.Id == model.Campaigns.Single(item => item.Id == publication.CampaignId)
            .Targets.Single(item => item.Id == publication.TargetId).SocialGroupId);
        await OpenFacebookAsync(publication.GroupUrl, group, (publication.CampaignId, publication.TargetId));
        var view = browser!;
        view.IsHitTestVisible = false;
        if (browserNavigation is not null)
            browserNavigation.IsHitTestVisible = false;
        browserAddress!.IsEnabled = false;
        prepareShareButton!.IsEnabled = false;
        outcomeButton!.IsEnabled = false;
        stopCampaignButton!.Visibility = Visibility.Visible;
        var token = Guid.NewGuid().ToString("N");
        var checkpoint = false;

        PublicationResult Stop(string message) => new(checkpoint ? TargetStatus.NeedsReview : TargetStatus.Failed, message);
        async Task<BrowserPublicationState> Inspect(string operation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (browser != view || publishingTarget != (publication.CampaignId, publication.TargetId))
                throw new ArgumentException("The campaign browser changed.");
            var source = view.CoreWebView2.Source;
            if (Validation.IsFacebookLoginPage(source))
                return new("authentication", "Sign in or complete Facebook's security check before resuming.", "");
            if (!Validation.IsSameFacebookGroup(publication.GroupUrl, source))
                return new("wrong-page", "Facebook navigated away from the selected group. The campaign stopped.", "");
            var request = JsonSerializer.Serialize(new { operation, token, groupUrl = publication.GroupUrl, postUrl = publication.PostUrl });
            var json = await view.CoreWebView2.ExecuteScriptAsync(FacebookPublishingScript.Source + $"({request})");
            return JsonSerializer.Deserialize<BrowserPublicationState>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new ArgumentException("Facebook did not return a publication state.");
        }

        // Navigation, dynamic composer loading and confirmation each have bounded waits.
        for (var pass = 0; !pageLoaded; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pass >= 90)
                return Stop("Facebook did not finish loading. Nothing was submitted.");
            await Task.Delay(500, cancellationToken);
        }
        BrowserPublicationState state = new("loading", "", "");
        for (var pass = 0; pass < 60; pass++)
        {
            state = await Inspect("prepare");
            if (state.Status == "ready")
                break;
            if (state.Status is not ("loading" or "opening" or "waiting"))
                return Stop(state.Message);
            await Task.Delay(500, cancellationToken);
        }
        if (state.Status != "ready")
            return Stop("The group editor was not ready within 30 seconds. Nothing was submitted.");
        cancellationToken.ThrowIfCancellationRequested();
        await beforeSubmit();
        checkpoint = true;
        state = await Inspect("submit");
        if (state.Status != "submitted")
            return Stop(state.Message.Length > 0 ? state.Message : "Submission was not confirmed. Check Facebook before retrying.");
        ShowAutomaticStatus(publication.GroupName, "Submitted once · waiting for Facebook confirmation…", InfoBarSeverity.Informational);
        for (var pass = 0; pass < 60; pass++)
        {
            await Task.Delay(500, cancellationToken);
            state = await Inspect("observe");
            if (state.Status == "published")
                return new(TargetStatus.Published, state.Message, state.Url);
            if (state.Status == "approval")
                return new(TargetStatus.AwaitingApproval, state.Message);
            if (state.Status != "waiting")
                return Stop(state.Message);
        }
        return Stop("Facebook did not confirm publication within 30 seconds. Check the group before retrying; the post will not be submitted again automatically.");
    }

    private sealed record BrowserPublicationState(string Status, string Message, string Url);

    private sealed class CampaignBrowser(MainWindow window) : ICampaignBrowser
    {
        public Task<PublicationResult> PublishAsync(CampaignPublication publication, Func<Task> beforeSubmit, CancellationToken cancellationToken) =>
            window.PublishInFacebookAsync(publication, beforeSubmit, cancellationToken);
    }
}

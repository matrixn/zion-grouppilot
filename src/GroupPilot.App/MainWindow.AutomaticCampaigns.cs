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
            automationConnection?.Dispose();
            automationConnection = null;
            automationPage = null;
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

    private async Task<PlaywrightGroupPublisher> CreatePublisherAsync(CampaignPublication publication)
    {
        var group = model.Groups.Single(item => item.Id == model.Campaigns.Single(item => item.Id == publication.CampaignId)
            .Targets.Single(item => item.Id == publication.TargetId).SocialGroupId);
        await OpenFacebookAsync(publication.GroupUrl, group, (publication.CampaignId, publication.TargetId), navigate: false, assisted: false);
        var view = browser!;
        view.IsHitTestVisible = false;
        if (browserNavigation is not null)
            browserNavigation.IsHitTestVisible = false;
        browserAddress!.IsEnabled = false;
        prepareShareButton!.IsEnabled = false;
        outcomeButton!.IsEnabled = false;
        stopCampaignButton!.Visibility = Visibility.Visible;
        var page = await GetPublishingPageAsync();
        return new PlaywrightGroupPublisher(page, stage =>
        {
            Serilog.Log.Information("Campaign target {TargetId}: {Stage}", publication.TargetId, stage);
            if (!stage.Contains(" · page=", StringComparison.Ordinal) && !stage.Contains(" · Playwright", StringComparison.Ordinal))
                ShowAutomaticStatus(publication.GroupName, stage, InfoBarSeverity.Informational);
        });
    }

    private WebViewAutomationConnection? automationConnection;
    private Microsoft.Playwright.IPage? automationPage;

    private async Task<string> GetBrowserTargetIdAsync()
    {
        var json = await ReadyBrowser().CallDevToolsProtocolMethodAsync("Target.getTargetInfo", "{}");
        using var data = JsonDocument.Parse(json);
        return data.RootElement.GetProperty("targetInfo").GetProperty("targetId").GetString()
            ?? throw new ArgumentException("The browser did not expose its target ID.");
    }

    private async Task<Microsoft.Playwright.IPage> GetPublishingPageAsync()
    {
        if (automationPage is { IsClosed: false })
            return automationPage;
        automationConnection?.Dispose();
        automationConnection = new WebViewAutomationConnection();
        automationPage = await automationConnection.ConnectAsync(browserDebugPort, await GetBrowserTargetIdAsync());
        return automationPage;
    }

    private async Task CheckPublishingConnectionAsync()
    {
        using var connection = new WebViewAutomationConnection();
        var page = await connection.ConnectAsync(browserDebugPort, await GetBrowserTargetIdAsync());
        if (page.IsClosed)
            throw new ArgumentException("The embedded browser is closed.");
        browserStatus!.Text = "Playwright connected to this embedded Facebook browser. No post was submitted.";
        Serilog.Log.Information("Playwright connection verified for the embedded Facebook WebView");
    }

    private sealed class CampaignBrowser(MainWindow window) : ICampaignBrowser, ICampaignResultVerifier
    {
        public async Task<PublicationResult> PublishAsync(CampaignPublication publication, Func<Task> beforeSubmit, CancellationToken cancellationToken) =>
            await (await window.CreatePublisherAsync(publication)).PublishAsync(publication, beforeSubmit, cancellationToken);

        public async Task<PublicationResult?> VerifyAsync(CampaignPublication publication, DateTimeOffset submittedAt, CancellationToken cancellationToken) =>
            await (await window.CreatePublisherAsync(publication)).VerifyAsync(publication, submittedAt, cancellationToken);
    }
}

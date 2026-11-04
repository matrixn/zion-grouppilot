using System.Text.Json;
using GroupPilot.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GroupPilot_App;

public sealed partial class MainWindow
{
    private Button? prepareShareButton;
    private bool preparingShare;

    private async Task TryPrepareShareAfterNavigationAsync(WebView2 view)
    {
        await Task.Delay(800);
        if (browser != view || publishingTarget is null || automaticCampaign is not null)
            return;
        try
        {
            await PrepareShareComposerAsync();
        }
        catch (ArgumentException ex)
        {
            if (browserStatus is not null)
                browserStatus.Text = ex.Message;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not prepare Facebook share");
            if (browserStatus is not null)
                browserStatus.Text = "Open the group's post editor, then choose Prepare selected share.";
        }
    }

    private async Task PrepareShareComposerAsync()
    {
        var context = publishingTarget ?? throw new ArgumentException("Start a campaign target first.");
        var campaign = model.Campaigns.SingleOrDefault(item => item.Id == context.CampaignId && !item.IsArchived)
            ?? throw new ArgumentException("Campaign unavailable.");
        var target = campaign.Targets.Single(item => item.Id == context.TargetId);
        var post = model.Posts.Single(item => item.Id == campaign.PostId);
        var source = CurrentFacebookPage();
        if (!Validation.IsSameFacebookGroup(target.GroupUrl, source))
            throw new ArgumentException("Return to the selected Facebook group before preparing its share.");
        if (!CanSharePost(post))
            throw new ArgumentException("The campaign needs a saved Facebook post.");

        if (preparingShare)
            return;
        var view = browser!;
        var core = ReadyBrowser();
        preparingShare = true;
        FacebookHost.IsEnabled = true;
        try
        {
            for (var pass = 0; pass < 20; pass++)
            {
                if (browser != view || publishingTarget != context || !pageLoaded || core.Source != source)
                    return;

                var result = await core.ExecuteScriptAsync("""
                    ((expectedUrl, postUrl) => {
                        if (location.href !== expectedUrl)
                            return 'changed';
                        const visible = element => element.getClientRects().length > 0;
                        const dialogs = Array.from(document.querySelectorAll('[role="dialog"]')).filter(visible);
                        const restricted = /temporarily blocked|you can't use this feature|going too fast|blocat.{0,6}temporar|nu po[tț]i folosi aceast[ăa] func[tț]ie|prea repede/i;
                        if (Array.from(document.querySelectorAll('[role="alert"], [role="dialog"]')).filter(visible)
                            .some(element => restricted.test(element.innerText)))
                            return 'restricted';
                        const postButton = /^(post|publish|public[ăa]|posteaz[ăa])$/i;
                        const createHeading = /^(create post|create a post|creeaz[ăa] o postare|creare postare)$/i;
                        const postDialogs = dialogs.filter(dialog =>
                            Array.from(dialog.querySelectorAll('[role="button"],button')).some(button =>
                                visible(button) && postButton.test((button.getAttribute('aria-label') || button.innerText || '').trim()))
                            || Array.from(dialog.querySelectorAll('[role="heading"],h1,h2')).some(heading =>
                                visible(heading) && createHeading.test(heading.innerText.trim())));
                        const expected = new URL(postUrl);
                        const hasPreview = postDialogs.some(dialog =>
                            Array.from(dialog.querySelectorAll('a[href]')).some(link => {
                                const url = new URL(link.href, location.href);
                                return url.protocol === 'https:' &&
                                    (url.hostname === 'facebook.com' || url.hostname.endsWith('.facebook.com')) &&
                                    url.pathname.replace(/\/$/, '') === expected.pathname.replace(/\/$/, '') &&
                                    ['story_fbid', 'fbid', 'v', 'id'].every(key =>
                                        !expected.searchParams.has(key) || url.searchParams.get(key) === expected.searchParams.get(key));
                            }));
                        if (hasPreview)
                            return 'ready';
                        const editors = Array.from(new Set(postDialogs.flatMap(dialog =>
                            Array.from(dialog.querySelectorAll('[contenteditable="true"][role="textbox"], [contenteditable="true"][data-lexical-editor="true"]')).filter(visible))));
                        if (editors.length === 1) {
                            const editor = editors[0];
                            const current = editor.innerText.trim();
                            if (current === postUrl)
                                return 'ready';
                            if (current.length > 0)
                                return 'occupied';
                            if (editor.dataset.groupPilotUrl === postUrl)
                                return 'waiting';
                            editor.focus();
                            editor.dataset.groupPilotUrl = postUrl;
                            document.execCommand('insertText', false, postUrl);
                            return editor.innerText.trim() === postUrl ? 'ready' : 'waiting';
                        }
                        if (dialogs.length > 0)
                            return 'manual';
                        const buttons = Array.from((document.querySelector('[role="main"]') ?? document)
                            .querySelectorAll('[role="button"],button')).filter(visible);
                        const labels = /^(write something|scrie ceva|scrie o postare|posteaz[ăa] ceva|create a post|creeaz[ăa] o postare)([.\s…]|$)/i;
                        const matches = buttons.filter(button => labels.test((button.innerText || button.getAttribute('aria-label') || '').trim()));
                        const compose = matches.filter(button => !matches.some(other => other !== button && button.contains(other)));
                        if (compose.length !== 1)
                            return 'loading';
                        compose[0].click();
                        return 'opening';
                    })
                    """ + $"({JsonSerializer.Serialize(source)}, {JsonSerializer.Serialize(post.Url)})");
                var status = JsonSerializer.Deserialize<string>(result);
                Serilog.Log.Information("Share preparation {Status} for target {TargetId}", status, target.Id);
                if (status is "opening" or "loading" or "waiting")
                {
                    await Task.Delay(600);
                    continue;
                }
                var message = status switch
                    {
                        "ready" => "The selected Facebook post link is ready. Review the preview and confirm Post in Facebook.",
                        "restricted" => "Facebook reported a restriction. Stop sharing and follow the message shown by Facebook.",
                        "occupied" => "The editor already contains text. Review it before posting.",
                        _ => "Open the group's post editor and choose Prepare selected share, or paste the copied link."
                    };
                if (browserStatus is not null)
                    browserStatus.Text = message;
                if (sharingBanner is not null)
                {
                    sharingBanner.IsOpen = true;
                    sharingBanner.Message = message;
                    sharingBanner.Severity = status == "ready" ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
                }
                return;
            }
            if (sharingBanner is not null)
            {
                sharingBanner.IsOpen = true;
                sharingBanner.Severity = InfoBarSeverity.Warning;
                sharingBanner.Message = "The group editor could not be opened automatically. Open Write something in Facebook, then choose Prepare selected share. The post link is on the clipboard.";
            }
        }
        finally
        {
            preparingShare = false;
            if (model.Busy)
                FacebookHost.IsEnabled = false;
        }
    }
}

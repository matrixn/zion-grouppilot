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
        if (browser != view || publishingTarget is null)
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
            for (var pass = 0; pass < 6; pass++)
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
                        const postDialogs = dialogs.filter(dialog => Array.from(dialog.querySelectorAll('[role="button"],button'))
                            .some(button => visible(button) && postButton.test((button.innerText || button.getAttribute('aria-label') || '').trim())));
                        const editors = postDialogs.flatMap(dialog =>
                            Array.from(dialog.querySelectorAll('[contenteditable="true"][role="textbox"]')).filter(visible));
                        if (editors.length === 1) {
                            const editor = editors[0];
                            const current = editor.innerText.trim();
                            if (current === postUrl)
                                return 'ready';
                            if (current.length > 0)
                                return 'occupied';
                            editor.focus();
                            if (!document.execCommand('insertText', false, postUrl))
                                return 'manual';
                            return editor.innerText.trim() === postUrl ? 'ready' : 'manual';
                        }
                        if (dialogs.length > 0)
                            return 'manual';
                        const buttons = Array.from((document.querySelector('[role="main"]') ?? document)
                            .querySelectorAll('[role="button"],button')).filter(visible);
                        const labels = /^(write something|scrie ceva|create a post|creeaz[ăa] o postare)([.\s…]|$)/i;
                        const compose = buttons.filter(button => labels.test((button.innerText || button.getAttribute('aria-label') || '').trim()));
                        if (compose.length !== 1)
                            return 'manual';
                        compose[0].click();
                        return 'opening';
                    })
                    """ + $"({JsonSerializer.Serialize(source)}, {JsonSerializer.Serialize(post.Url)})");
                var status = JsonSerializer.Deserialize<string>(result);
                if (status == "opening")
                {
                    await Task.Delay(600);
                    continue;
                }
                if (browserStatus is not null)
                    browserStatus.Text = status switch
                    {
                        "ready" => "The selected Facebook post link is ready. Review the preview and confirm Post in Facebook.",
                        "restricted" => "Facebook reported a restriction. Stop sharing and follow the message shown by Facebook.",
                        "occupied" => "The editor already contains text. Review it before posting.",
                        _ => "Open the group's post editor and choose Prepare selected share, or paste the copied link."
                    };
                return;
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

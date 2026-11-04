using System.Diagnostics;
using System.Text.Json;
using GroupPilot.Core;
using Microsoft.Playwright;

namespace GroupPilot.Infrastructure;

public sealed record PublishingTimeouts(int PrepareMs = 45000, int ConfirmMs = 45000, int VerifyAfterMs = 15000, int ActionMs = 1500);

public sealed class PlaywrightGroupPublisher(IPage page, Action<string>? report = null, PublishingTimeouts? timeouts = null) : ICampaignBrowser, ICampaignResultVerifier
{
    private readonly PublishingTimeouts limits = timeouts ?? new();

    private async Task<FacebookPageSnapshot> ReadSnapshotAsync(CampaignPublication publication, string token)
    {
        // Explicit JSON avoids CDP's inferred array types for empty/nested DOM collections.
        try
        {
            var json = await page.EvaluateAsync<string>("request => JSON.stringify((" + FacebookPageSnapshotScript.Source + ")(request))",
                new { token, groupUrl = publication.GroupUrl, postUrl = publication.PostUrl });
            return JsonSerializer.Deserialize<FacebookPageSnapshot>(json) ?? throw new InvalidOperationException("Facebook returned no page state.");
        }
        catch (PlaywrightException ex) when (!page.IsClosed && (ex.Message.Contains("Execution context was destroyed", StringComparison.Ordinal)
            || ex.Message.Contains("Cannot find context", StringComparison.Ordinal)))
        {
            return new() { Status = "loading", Message = "Facebook is changing the document." };
        }
    }

    public async Task<PublicationResult?> VerifyAsync(CampaignPublication publication, DateTimeOffset submittedAt, CancellationToken cancellationToken)
    {
        report?.Invoke("Verifying the previous submission in the group feed; no Publish click will be made");
        cancellationToken.ThrowIfCancellationRequested();
        await page.GotoAsync(publication.GroupUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = limits.PrepareMs }).WaitAsync(cancellationToken);
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < limits.PrepareMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Validation.IsSameFacebookGroup(publication.GroupUrl, page.Url)) return null;
            var current = await ReadSnapshotAsync(publication, "verification");
            if (current.Status == "loading") { await Task.Delay(300, cancellationToken); continue; }
            if (current.Status != "ok") return null;
            // Existing generic notices cannot prove a previous submission. Only a matching recent group permalink can.
            var found = FacebookPublicationEvidence.Observe(publication, new() { Notices = current.Notices }, current, submittedAt);
            if (found is not null) return found with { Evidence = "Recovered previous submission: " + found.Evidence };
            await Task.Delay(500, cancellationToken);
        }
        report?.Invoke("No reliable match found for the previous submission. NeedsReview was kept; no post was sent");
        return null;
    }

    public async Task<PublicationResult> PublishAsync(CampaignPublication publication, Func<Task> beforeSubmit, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");
        var checkpoint = false;
        var stage = "opening the selected group";
        var submittedAt = DateTimeOffset.MinValue;
        PublicationResult Stop(string reason) => new(checkpoint ? TargetStatus.NeedsReview : TargetStatus.Failed, $"Stopped while {stage}: {reason}");
        void Stage(string value) { stage = value; report?.Invoke(value); }
        ILocator Control(string kind) => page.Locator($"[data-grouppilot-{kind}='{token}']");
        async Task<FacebookPageSnapshot> Snapshot()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Validation.IsFacebookLoginPage(page.Url))
                return new() { Status = "authentication", Message = "Facebook requires login or a security check." };
            if (!Validation.IsSameFacebookGroup(publication.GroupUrl, page.Url))
                return new() { Status = "wrong-page", Message = "Facebook is no longer showing the selected group." };
            var value = await ReadSnapshotAsync(publication, token);
            report?.Invoke($"{stage} · page={value.Status}, dialogs={value.DialogCount}, composers={value.ComposerCount}, editors={value.EditorCount}, publish buttons={value.SubmitCount}, group posts={value.Articles.Length}");
            return value;
        }
        static bool SourceReady(FacebookPageSnapshot state, string url) =>
            state.ComposerCount == 1 && state.EditorCount == 1 && state.SubmitCount == 1 &&
            (state.EditorText == url || (state.EditorText.Length == 0 && state.HasSource));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stage(stage);
            await page.GotoAsync(publication.GroupUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = limits.PrepareMs }).WaitAsync(cancellationToken);
            var watch = Stopwatch.StartNew();
            var entered = false;
            var filled = false;
            FacebookPageSnapshot baseline = new();
            while (watch.ElapsedMilliseconds < limits.PrepareMs)
            {
                var state = await Snapshot();
                if (state.Status == "loading") { await Task.Delay(300, cancellationToken); continue; }
                if (state.Status != "ok") return Stop(state.Message);
                if (state.ComposerCount > 1 || state.EditorCount > 1 || state.SubmitCount > 1)
                    return Stop("Facebook exposed multiple matching editors or Publish buttons. Nothing was submitted.");
                if (state.ComposerCount == 1)
                {
                    if (state.EditorText.Length > 0 && state.EditorText != publication.PostUrl)
                        return Stop("The editor contains a different draft. It was left unchanged.");
                    if (SourceReady(state, publication.PostUrl))
                    {
                        Stage("checking that Publish is visible, stable and enabled");
                        cancellationToken.ThrowIfCancellationRequested();
                        try { await Control("submit").ClickAsync(new() { Trial = true, Timeout = limits.ActionMs }); }
                        catch (TimeoutException) { await Task.Delay(300, cancellationToken); continue; }
                        baseline = await Snapshot();
                        if (SourceReady(baseline, publication.PostUrl)) break;
                    }
                    else if (!filled && state.EditorCount == 1 && !state.HasSource)
                    {
                        Stage("entering the selected post link");
                        cancellationToken.ThrowIfCancellationRequested();
                        await Control("editor").FillAsync(publication.PostUrl, new() { Timeout = limits.ActionMs });
                        filled = true;
                    }
                }
                else if (!entered && state.DialogCount == 0 && state.EntryCount == 1)
                {
                    Stage("opening Facebook's group composer");
                    cancellationToken.ThrowIfCancellationRequested();
                    await Control("entry").ClickAsync(new() { Timeout = limits.ActionMs });
                    entered = true;
                }
                else
                    Stage(state.DialogCount > 0 ? "waiting for Facebook's dialog/editor" : "waiting for the group composer");
                await Task.Delay(300, cancellationToken);
            }
            if (!SourceReady(baseline, publication.PostUrl))
                return Stop("The expected editor was not ready before the timeout. Nothing was submitted.");
            cancellationToken.ThrowIfCancellationRequested();
            Stage("saving the submission checkpoint");
            await beforeSubmit();
            checkpoint = true;
            cancellationToken.ThrowIfCancellationRequested();
            var final = await Snapshot();
            if (final.Status != "ok" || !SourceReady(final, publication.PostUrl))
                return Stop("The page or draft changed before the click. Check Facebook before retrying.");
            Stage("clicking Publish once");
            submittedAt = DateTimeOffset.UtcNow;
            // One real input action. Never retry this call after a timeout or an uncertain navigation.
            await Control("submit").ClickAsync(new() { Timeout = limits.ActionMs });
            Stage("checking Facebook's confirmation and the group feed");
            watch.Restart();
            var refreshed = false;
            while (watch.ElapsedMilliseconds < limits.ConfirmMs)
            {
                var state = await Snapshot();
                if (state.Status == "loading") { await Task.Delay(300, cancellationToken); continue; }
                if (state.Status != "ok") return Stop(state.Message);
                var outcome = FacebookPublicationEvidence.Observe(publication, baseline, state, submittedAt);
                if (outcome is not null) return outcome;
                if (!refreshed && watch.ElapsedMilliseconds >= limits.VerifyAfterMs && state.ComposerCount == 0 && state.DialogCount == 0)
                {
                    Stage("reloading the group to verify the new post");
                    cancellationToken.ThrowIfCancellationRequested();
                    await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = limits.PrepareMs }).WaitAsync(cancellationToken);
                    refreshed = true;
                }
                await Task.Delay(500, cancellationToken);
            }
            return Stop("No reliable publication or moderation confirmation was found, including the group-feed check. Use Record selected outcome after checking Facebook; Run will not submit this target again.");
        }
        catch (OperationCanceledException)
        {
            return Stop(checkpoint ? "Stopped after the submission checkpoint. Check Facebook before retrying." : "Stopped before submission. Nothing was sent by this run.");
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            var detail = ex.Message.Split('\n')[0].Trim();
            report?.Invoke($"{stage} · Playwright {ex.GetType().Name}: {detail[..Math.Min(240, detail.Length)]}");
            return Stop(checkpoint ? "The browser action did not finish reliably. Check Facebook before retrying." : "The expected control was not actionable. Nothing was submitted.");
        }
    }
}

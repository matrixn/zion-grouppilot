using GroupPilot.Core;
using Microsoft.EntityFrameworkCore;

namespace GroupPilot.Infrastructure;

public sealed class CampaignRunner(Storage storage, ICampaignBrowser browser, TimeProvider? timeProvider = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<CampaignRunResult> RunAsync(int campaignId, int runNumber,
        IProgress<CampaignRunProgress>? progress, CancellationToken cancellationToken)
    {
        var initial = await LoadAsync(campaignId, runNumber);
        var ids = initial.Targets.Where(target => !target.IsExcluded)
            .OrderBy(target => target.Status is TargetStatus.InProgress or TargetStatus.NeedsReview ? 0 : 1)
            .ThenBy(target => target.Id).Select(target => target.Id).ToArray();
        var completed = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var campaign = await LoadAsync(campaignId, runNumber);
            var target = campaign.Targets.Single(item => item.Id == id);
            if (target.IsExcluded)
                throw new ArgumentException("The campaign selection changed. Start it again after reviewing the groups.");
            if (target.Status is TargetStatus.Published or TargetStatus.Skipped or TargetStatus.AwaitingApproval)
            {
                completed++;
                continue;
            }
            if (target.Status is TargetStatus.InProgress or TargetStatus.NeedsReview || target.SubmissionStartedAt is not null)
                return new(false, $"Check the previous submission to {target.GroupName} and record its outcome before resuming. It will not be submitted again automatically.");

            while (await storage.GetNextShareAtAsync(target.GroupUrl) is { } next && next > clock.GetUtcNow())
            {
                var remaining = next - clock.GetUtcNow();
                progress?.Report(new(completed, ids.Length, target.GroupName,
                    $"Pause between groups · {Math.Ceiling(remaining.TotalSeconds)} seconds remaining"));
                var interval = remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
                await (delay?.Invoke(interval, cancellationToken) ?? Task.Delay(interval, cancellationToken));
            }
            cancellationToken.ThrowIfCancellationRequested();
            var source = await storage.QueryAsync(db => db.Posts.AsNoTracking().SingleAsync(post => post.Id == campaign.PostId));
            if (source.Kind != PostKind.Existing || Validation.FacebookPostImportUrl(source.Url) is null)
                throw new ArgumentException("Choose an imported Facebook post with a direct post URL.");
            await storage.BeginTargetAsync(id, runNumber);
            progress?.Report(new(completed, ids.Length, target.GroupName, "Opening the group and preparing the selected post…"));
            PublicationResult result;
            try
            {
                result = await browser.PublishAsync(new(campaignId, id, runNumber, target.GroupUrl, target.GroupName, source.Url),
                    () => storage.MarkAutomaticSubmissionAsync(id, runNumber), cancellationToken);
            }
            catch (Exception ex)
            {
                var checkpoint = await storage.QueryAsync(db => db.Targets.AsNoTracking().SingleAsync(item => item.Id == id));
                var uncertain = checkpoint.SubmissionStartedAt is not null;
                result = new(uncertain ? TargetStatus.NeedsReview : TargetStatus.Failed,
                    ex is OperationCanceledException
                        ? uncertain ? "Stopped after the submission checkpoint. Check Facebook before retrying." : "Stopped before submission. Nothing was sent by this run."
                        : uncertain ? "The browser stopped responding after submission started. Check Facebook before retrying." : "The browser failed before submission. Nothing was sent by this run.");
            }
            await storage.RecordAutomaticOutcomeAsync(id, runNumber, result);
            progress?.Report(new(completed, ids.Length, target.GroupName, result.Evidence));
            if (result.Status is not (TargetStatus.Published or TargetStatus.AwaitingApproval))
                return new(false, result.Evidence);
            completed++;
        }
        return new(true, $"Campaign finished · {completed} groups processed. Posts awaiting approval remain marked separately.");
    }

    private Task<Campaign> LoadAsync(int id, int runNumber) => storage.QueryAsync(async db =>
    {
        var campaign = await db.Campaigns.AsNoTracking().Include(item => item.Targets)
            .SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived)
            ?? throw new ArgumentException("This campaign is no longer available.");
        if (campaign.RunNumber != runNumber)
            throw new ArgumentException("The campaign changed. Review it before running again.");
        return campaign;
    });
}

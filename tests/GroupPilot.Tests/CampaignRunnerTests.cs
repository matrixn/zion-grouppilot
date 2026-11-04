using GroupPilot.Core;
using GroupPilot.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GroupPilot.Tests;

public sealed class CampaignRunnerTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("grouppilot-runner-").FullName;
    private readonly TestClock clock = new();
    private Storage Open() => new(new Factory(new DbContextOptionsBuilder<PilotDb>()
        .UseSqlite($"Data Source={Path.Combine(folder, "runner.db")};Pooling=False").Options), clock);

    private async Task<(Storage Store, int CampaignId)> SetupAsync()
    {
        var store = Open();
        await store.InitializeAsync();
        await store.SavePostAsync(new Post { Title = "Imported", Kind = PostKind.Existing, Url = "https://www.facebook.com/source/posts/123/" });
        await store.SaveGroupAsync(new SocialGroup { Name = "First", Url = "https://www.facebook.com/groups/first/" }, "");
        await store.SaveGroupAsync(new SocialGroup { Name = "Second", Url = "https://www.facebook.com/groups/second/" }, "");
        var ids = await store.QueryAsync(db => db.Groups.OrderBy(group => group.Id).Select(group => group.Id).ToArrayAsync());
        var id = await store.SaveCampaignAsync("Automatic", 1, ids, null, 3);
        return (store, id);
    }

    private Task Delay(TimeSpan duration, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        clock.Now += duration;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ConfirmedResultsPersistAndPauseBetweenGroups()
    {
        var (store, id) = await SetupAsync();
        var times = new List<DateTimeOffset>();
        var browser = new Browser(async (publication, checkpoint, _) =>
        {
            times.Add(clock.Now);
            await checkpoint();
            return publication.GroupName == "First"
                ? new(TargetStatus.Published, "Facebook confirmed publication.", publication.GroupUrl + "posts/456/")
                : new(TargetStatus.AwaitingApproval, "Facebook confirmed submission for approval.");
        });
        var result = await new CampaignRunner(store, browser, clock, Delay).RunAsync(id, 1, null, default);
        Assert.True(result.Completed);
        Assert.True(times[1] - times[0] >= TimeSpan.FromSeconds(3));
        var history = await Open().QueryAsync(db => db.Attempts.OrderBy(attempt => attempt.Id).ToArrayAsync());
        Assert.Equal(2, history.Length);
        Assert.All(history, attempt => { Assert.True(attempt.IsAutomatic); Assert.NotEmpty(attempt.Evidence); });
        Assert.Equal(TargetStatus.Published, history[0].Status);
        Assert.Equal(TargetStatus.AwaitingApproval, history[1].Status);
    }

    [Fact]
    public async Task UnknownSubmissionSurvivesRestartAndIsNeverRetried()
    {
        var (store, id) = await SetupAsync();
        var calls = 0;
        var browser = new Browser(async (_, checkpoint, _) =>
        {
            calls++;
            await checkpoint();
            return new(TargetStatus.NeedsReview, "No confirmation after submission.");
        });
        Assert.False((await new CampaignRunner(store, browser, clock, Delay).RunAsync(id, 1, null, default)).Completed);
        store = Open();
        Assert.False((await new CampaignRunner(store, browser, clock, Delay).RunAsync(id, 1, null, default)).Completed);
        Assert.Equal(1, calls);
        var target = await store.QueryAsync(db => db.Targets.OrderBy(target => target.Id).FirstAsync());
        Assert.Equal(TargetStatus.NeedsReview, target.Status);
        Assert.NotNull(target.SubmissionStartedAt);
        await Assert.ThrowsAsync<ArgumentException>(() => store.BeginTargetAsync(target.Id, 1));
    }

    [Fact]
    public async Task BrowserCrashAfterCheckpointRequiresReviewInsteadOfRetry()
    {
        var (store, id) = await SetupAsync();
        var browser = new Browser(async (_, checkpoint, _) => { await checkpoint(); throw new IOException("Lost browser"); });
        await new CampaignRunner(store, browser, clock, Delay).RunAsync(id, 1, null, default);
        Assert.Equal(TargetStatus.NeedsReview, await store.QueryAsync(db => db.Targets.OrderBy(target => target.Id).Select(target => target.Status).FirstAsync()));
        Assert.Single(await store.QueryAsync(db => db.Attempts.ToArrayAsync()));
    }

    [Fact]
    public async Task PreparationFailureDoesNotPretendSubmissionOccurred()
    {
        var (store, id) = await SetupAsync();
        var browser = new Browser((_, _, _) => Task.FromResult(new PublicationResult(TargetStatus.Failed, "Facebook requires login.")));
        Assert.False((await new CampaignRunner(store, browser, clock, Delay).RunAsync(id, 1, null, default)).Completed);
        var targets = await store.QueryAsync(db => db.Targets.OrderBy(target => target.Id).ToArrayAsync());
        Assert.Equal(TargetStatus.Failed, targets[0].Status);
        Assert.Null(targets[0].SubmissionStartedAt);
        Assert.Equal(TargetStatus.Pending, targets[1].Status);
    }

    [Fact]
    public async Task InterruptedLegacyTargetRequiresManualReview()
    {
        var (store, id) = await SetupAsync();
        var targetId = await store.QueryAsync(db => db.Targets.Select(target => target.Id).FirstAsync());
        await store.BeginTargetAsync(targetId, 1);
        var browser = new Browser((_, _, _) => throw new InvalidOperationException("Must not reach browser"));
        Assert.False((await new CampaignRunner(store, browser, clock, Delay).RunAsync(id, 1, null, default)).Completed);
        Assert.Empty(await store.QueryAsync(db => db.Attempts.ToArrayAsync()));
    }

    [Fact]
    public async Task CheckpointAndEvidenceAreRequiredForAutomaticSuccess()
    {
        var (store, _) = await SetupAsync();
        var targetId = await store.QueryAsync(db => db.Targets.Select(target => target.Id).FirstAsync());
        await store.BeginTargetAsync(targetId, 1);
        var confirmed = new PublicationResult(TargetStatus.Published, "Facebook confirmed publication.");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordAutomaticOutcomeAsync(targetId, 1, confirmed));
        await store.MarkAutomaticSubmissionAsync(targetId, 1);
        await Assert.ThrowsAsync<ArgumentException>(() => store.MarkAutomaticSubmissionAsync(targetId, 1));
        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordAutomaticOutcomeAsync(targetId, 1,
            confirmed with { PublishedUrl = "https://www.facebook.com/groups/other/posts/456/" }));
        await store.RecordAutomaticOutcomeAsync(targetId, 1, confirmed);
        Assert.Equal(TargetStatus.Published, await store.QueryAsync(db => db.Targets.Where(target => target.Id == targetId).Select(target => target.Status).SingleAsync()));
    }

    [Fact]
    public async Task StopDuringPauseDoesNotOpenTheNextGroup()
    {
        var (store, id) = await SetupAsync();
        using var cancel = new CancellationTokenSource();
        var calls = 0;
        var browser = new Browser(async (_, checkpoint, _) => { calls++; await checkpoint(); return new(TargetStatus.Published, "Facebook confirmed publication."); });
        Task Stop(TimeSpan _, CancellationToken token) { cancel.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CampaignRunner(store, browser, clock, Stop).RunAsync(id, 1, null, cancel.Token));
        Assert.Equal(1, calls);
        Assert.Equal(TargetStatus.Pending, await store.QueryAsync(db => db.Targets.OrderByDescending(target => target.Id).Select(target => target.Status).FirstAsync()));
    }

    [Fact]
    public async Task ManualReviewCanReleaseAnUncertainTargetForAnExplicitRetry()
    {
        var (store, id) = await SetupAsync();
        var targetId = await store.QueryAsync(db => db.Targets.Select(target => target.Id).FirstAsync());
        await store.BeginTargetAsync(targetId, 1);
        await store.MarkAutomaticSubmissionAsync(targetId, 1);
        await store.RecordAutomaticOutcomeAsync(targetId, 1, new(TargetStatus.NeedsReview, "Check Facebook."));
        await store.RecordAttemptAsync(targetId, TargetStatus.Failed, "", 1);
        var browser = new Browser(async (_, checkpoint, _) => { await checkpoint(); return new(TargetStatus.Published, "Facebook confirmed publication."); });
        Assert.True((await new CampaignRunner(store, browser, clock, Delay).RunAsync(id, 1, null, default)).Completed);
    }

    [Fact]
    public async Task RecoveryRecordsExistingPostAndOnlyPublishesRemainingGroups()
    {
        var (store, id) = await SetupAsync();
        var targetId = await store.QueryAsync(db => db.Targets.OrderBy(target => target.Id).Select(target => target.Id).FirstAsync());
        await store.BeginTargetAsync(targetId, 1);
        await store.MarkAutomaticSubmissionAsync(targetId, 1);
        await store.RecordAutomaticOutcomeAsync(targetId, 1, new(TargetStatus.NeedsReview, "No confirmation."));
        var browser = new VerifyingBrowser(true);
        Assert.True((await new CampaignRunner(store, browser, clock, Delay).RunAsync(id, 1, null, default)).Completed);
        Assert.Equal(1, browser.Verifications);
        Assert.Equal(1, browser.Publications);
        Assert.All(await store.QueryAsync(db => db.Targets.ToArrayAsync()), target => Assert.Equal(TargetStatus.Published, target.Status));
        Assert.Equal(3, await store.QueryAsync(db => db.Attempts.CountAsync()));
    }

    [Fact]
    public async Task UnconfirmedRecoveryDoesNotSubmitOrClearNeedsReview()
    {
        var (store, id) = await SetupAsync();
        var targetId = await store.QueryAsync(db => db.Targets.OrderBy(target => target.Id).Select(target => target.Id).FirstAsync());
        await store.BeginTargetAsync(targetId, 1);
        await store.MarkAutomaticSubmissionAsync(targetId, 1);
        await store.RecordAutomaticOutcomeAsync(targetId, 1, new(TargetStatus.NeedsReview, "No confirmation."));
        var browser = new VerifyingBrowser(false);
        Assert.False((await new CampaignRunner(store, browser, clock, Delay).RunAsync(id, 1, null, default)).Completed);
        Assert.Equal(1, browser.Verifications);
        Assert.Equal(0, browser.Publications);
        Assert.Equal(TargetStatus.NeedsReview, await store.QueryAsync(db => db.Targets.Where(target => target.Id == targetId).Select(target => target.Status).SingleAsync()));
    }

    private sealed class VerifyingBrowser(bool found) : ICampaignBrowser, ICampaignResultVerifier
    {
        public int Verifications { get; private set; }
        public int Publications { get; private set; }
        public Task<PublicationResult?> VerifyAsync(CampaignPublication publication, DateTimeOffset submittedAt, CancellationToken cancellationToken)
        {
            Verifications++;
            return Task.FromResult<PublicationResult?>(found ? new(TargetStatus.Published, "Found the previous group post.", publication.GroupUrl + "posts/456/") : null);
        }
        public async Task<PublicationResult> PublishAsync(CampaignPublication publication, Func<Task> beforeSubmit, CancellationToken cancellationToken)
        {
            Publications++;
            await beforeSubmit();
            return new(TargetStatus.Published, "Facebook confirmed publication.");
        }
    }

    private sealed class Browser(Func<CampaignPublication, Func<Task>, CancellationToken, Task<PublicationResult>> publish) : ICampaignBrowser
    {
        public Task<PublicationResult> PublishAsync(CampaignPublication publication, Func<Task> beforeSubmit, CancellationToken cancellationToken) => publish(publication, beforeSubmit, cancellationToken);
    }
    private sealed class Factory(DbContextOptions<PilotDb> options) : IDbContextFactory<PilotDb>
    {
        public PilotDb CreateDbContext() => new(options);
    }
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    public void Dispose() => Directory.Delete(folder, true);
}

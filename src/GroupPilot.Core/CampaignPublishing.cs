namespace GroupPilot.Core;

public sealed record CampaignPublication(int CampaignId, int TargetId, int RunNumber,
    string GroupUrl, string GroupName, string PostUrl);

public sealed record PublicationResult(TargetStatus Status, string Evidence, string PublishedUrl = "");

public interface ICampaignBrowser
{
    // The durable checkpoint must complete before the browser clicks Publish.
    Task<PublicationResult> PublishAsync(CampaignPublication publication, Func<Task> beforeSubmit,
        CancellationToken cancellationToken);
}

public interface ICampaignResultVerifier
{
    // Read-only recovery: never press Publish while checking an earlier submission.
    Task<PublicationResult?> VerifyAsync(CampaignPublication publication, DateTimeOffset submittedAt, CancellationToken cancellationToken);
}

public sealed record CampaignRunProgress(int Completed, int Total, string GroupName, string Message);
public sealed record CampaignRunResult(bool Completed, string Message);

public static class FacebookPageSnapshotScript
{
    public static string Source { get; } = Read();

    private static string Read()
    {
        using var stream = typeof(FacebookPageSnapshotScript).Assembly
            .GetManifestResourceStream("GroupPilot.Core.FacebookPageSnapshot.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

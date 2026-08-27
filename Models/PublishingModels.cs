namespace Hugoer.Models;

/// <summary>Final route used by a publish attempt.</summary>
public enum PublishOutcome
{
    GitHubPages,
    LocalFallback,
    Local,
    Failed
}

/// <summary>
/// Describes both the requested route and the route that actually completed.
/// Keeping this result richer than a bare exit code lets the UI explain when a
/// remote-first publish had to fall back to a local build.
/// </summary>
public sealed class PublishResult
{
    public DeploymentMode RequestedMode { get; init; }
    public PublishOutcome Outcome { get; init; }
    public bool RemoteAttempted { get; init; }
    public bool RemotePushSucceeded { get; init; }
    public CommandResult? RemoteResult { get; init; }
    public CommandResult? LocalResult { get; init; }
    public string Message { get; init; } = string.Empty;

    public bool Succeeded => Outcome is not PublishOutcome.Failed;
    public bool UsedLocalFallback => Outcome == PublishOutcome.LocalFallback;
}

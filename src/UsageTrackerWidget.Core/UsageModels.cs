namespace UsageTrackerWidget.Core;

public static class UsageWindowKeys
{
    public const string CodexSession = "codex.session";
    public const string CodexWeekly = "codex.weekly";
    public const string CodexDailyImages = "codex.images.daily";
}

public enum UsageErrorKind
{
    Unknown,
    Authentication,
    Network,
    Http,
    InvalidResponse,
    ContractChanged,
}

public sealed record UsageWindow(
    string Key,
    string DisplayName,
    double? UsedPercent,
    TimeSpan? WindowLength,
    DateTimeOffset? ResetsAt,
    bool IsLimitReached = false)
{
    private static readonly TimeSpan IdleTolerance = TimeSpan.FromMinutes(2);

    // An unstarted window reports 0% with its full length still remaining; the countdown only
    // begins with the first metered request.
    public bool IsClockIdle(DateTimeOffset observedAt)
    {
        return UsedPercent == 0 &&
            !IsLimitReached &&
            WindowLength is { } length &&
            ResetsAt is { } resetsAt &&
            resetsAt - observedAt >= length - IdleTolerance;
    }
}

public sealed record UsageSourceError(
    string SourceKey,
    string DisplayName,
    UsageErrorKind Kind,
    int? StatusCode,
    string Message,
    DateTimeOffset ObservedAt);

public sealed record UsageSnapshot(
    string ProviderKey,
    string ProviderDisplayName,
    string AccountLabel,
    string? PlanLabel,
    DateTimeOffset ObservedAt,
    IReadOnlyList<UsageWindow> Windows);

public sealed record UsageReadResult(
    UsageSnapshot Snapshot,
    IReadOnlyList<UsageSourceError> Errors);

public interface IUsageProvider
{
    Task<UsageReadResult> ReadAsync(CancellationToken cancellationToken);
}

// Deliberately spends a minimal amount of quota so an idle window's countdown begins.
public interface IUsageWindowStarter
{
    bool CanStart(string windowKey);

    Task StartAsync(string windowKey, CancellationToken cancellationToken);
}

public sealed class UsageWindowStartException : Exception
{
    public UsageWindowStartException(string message, int? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}

public sealed class UsageAuthenticationRequiredException : Exception
{
    public UsageAuthenticationRequiredException(string message)
        : base(message)
    {
    }
}

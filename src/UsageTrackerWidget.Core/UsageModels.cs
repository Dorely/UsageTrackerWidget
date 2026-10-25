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
    bool IsLimitReached = false);

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

public sealed class UsageAuthenticationRequiredException : Exception
{
    public UsageAuthenticationRequiredException(string message)
        : base(message)
    {
    }
}

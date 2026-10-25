namespace UsageTrackerWidget.App.Storage;

public sealed record UsageHistoryPoint(
    DateTimeOffset ObservedAt,
    double UsedPercent,
    bool IsReset);

internal sealed record LatestUsageSample(
    string ProviderKey,
    string WindowKey,
    string DisplayName,
    double? UsedPercent,
    TimeSpan? WindowLength,
    DateTimeOffset? ResetsAt,
    bool IsLimitReached,
    DateTimeOffset ObservedAt,
    string? PlanLabel);

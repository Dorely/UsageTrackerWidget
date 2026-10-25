namespace UsageTrackerWidget.Providers.Codex;

internal sealed record CodexTokens(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt,
    string? Scope);

internal sealed record CodexCredentials(
    string AccessToken,
    string AccountId,
    DateTimeOffset ExpiresAt);

internal sealed record StoredCodexTokens(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt,
    string? Scope);

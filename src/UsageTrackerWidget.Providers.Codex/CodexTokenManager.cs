namespace UsageTrackerWidget.Providers.Codex;

public sealed class CodexTokenManager
{
    private readonly ICodexTokenStore _store;
    private readonly CodexOAuthService _oauth;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public CodexTokenManager(HttpClient httpClient)
        : this(new DpapiCodexTokenStore(), new CodexOAuthService(httpClient))
    {
    }

    internal CodexTokenManager(ICodexTokenStore store, CodexOAuthService oauth)
    {
        _store = store;
        _oauth = oauth;
    }

    public async Task<bool> HasStoredCredentialAsync(CancellationToken cancellationToken = default)
    {
        return await _store.LoadAsync(cancellationToken) is not null;
    }

    public async Task LoginAsync(
        Action<string, bool>? authorizationReady,
        CancellationToken cancellationToken = default)
    {
        var tokens = await _oauth.AuthorizeAsync(authorizationReady, cancellationToken);
        _ = CodexAccountIdentity.ReadAccountId(tokens.AccessToken);

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            await SaveAsync(tokens, cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    internal async Task<CodexCredentials> GetCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var current = await LoadRequiredAsync(cancellationToken);
        if (NeedsRefresh(current))
        {
            current = await RefreshLockedAsync(rejectedAccessToken: null, cancellationToken);
        }

        return ToCredentials(current);
    }

    internal async Task<CodexCredentials> RecoverFromUnauthorizedAsync(
        string rejectedAccessToken,
        CancellationToken cancellationToken = default)
    {
        var current = await LoadRequiredAsync(cancellationToken);
        if (!string.Equals(current.AccessToken, rejectedAccessToken, StringComparison.Ordinal) &&
            !NeedsRefresh(current))
        {
            return ToCredentials(current);
        }

        current = await RefreshLockedAsync(rejectedAccessToken, cancellationToken);
        return ToCredentials(current);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            await _store.ClearAsync(cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<StoredCodexTokens> RefreshLockedAsync(
        string? rejectedAccessToken,
        CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadRequiredAsync(cancellationToken);

            if (rejectedAccessToken is not null &&
                !string.Equals(current.AccessToken, rejectedAccessToken, StringComparison.Ordinal) &&
                !NeedsRefresh(current))
            {
                return current;
            }

            if (rejectedAccessToken is null && !NeedsRefresh(current))
            {
                return current;
            }

            if (string.IsNullOrWhiteSpace(current.RefreshToken))
            {
                await _store.ClearAsync(cancellationToken);
                throw new CodexReauthRequiredException(
                    "The stored Codex credential cannot be refreshed. Sign in to Codex again.");
            }

            CodexTokens refreshed;
            try
            {
                refreshed = await _oauth.RefreshAsync(current.RefreshToken, cancellationToken);
            }
            catch (CodexReauthRequiredException)
            {
                await _store.ClearAsync(cancellationToken);
                throw new CodexReauthRequiredException(
                    "Codex rejected the stored login. Sign in to Codex again.");
            }

            await SaveAsync(refreshed, cancellationToken);
            return ToStored(refreshed);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<StoredCodexTokens> LoadRequiredAsync(CancellationToken cancellationToken)
    {
        return await _store.LoadAsync(cancellationToken)
            ?? throw new CodexReauthRequiredException("Connect Codex to start tracking usage.");
    }

    private async Task SaveAsync(CodexTokens tokens, CancellationToken cancellationToken)
    {
        await _store.SaveAsync(ToStored(tokens), cancellationToken);
    }

    private static bool NeedsRefresh(StoredCodexTokens tokens)
    {
        return tokens.ExpiresAt - CodexConstants.TokenRefreshMargin <= DateTimeOffset.UtcNow;
    }

    private static StoredCodexTokens ToStored(CodexTokens tokens)
    {
        return new StoredCodexTokens(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAt, tokens.Scope);
    }

    private static CodexCredentials ToCredentials(StoredCodexTokens tokens)
    {
        return new CodexCredentials(
            tokens.AccessToken,
            CodexAccountIdentity.ReadAccountId(tokens.AccessToken),
            tokens.ExpiresAt);
    }
}

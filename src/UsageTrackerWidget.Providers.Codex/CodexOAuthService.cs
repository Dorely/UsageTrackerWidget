using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UsageTrackerWidget.Providers.Codex;

public sealed class CodexOAuthService
{
    private static readonly HashSet<string> RejectedGrantCodes = new(StringComparer.Ordinal)
    {
        "invalid_grant",
        "invalid_token",
        "refresh_token_expired",
        "refresh_token_reused",
        "refresh_token_invalidated",
    };

    private readonly HttpClient _httpClient;

    public CodexOAuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    internal async Task<CodexTokens> AuthorizeAsync(
        Action<string, bool>? authorizationReady,
        CancellationToken cancellationToken)
    {
        var pkce = CodexPkce.Create();
        var callbackTask = LoopbackOAuthServer.WaitAsync(
            CodexConstants.LoopbackPort,
            CodexConstants.LoginTimeout,
            cancellationToken);
        var authorizeUrl = CodexPkce.BuildAuthorizeUrl(pkce);
        var browserOpened = TryOpenBrowser(authorizeUrl);
        authorizationReady?.Invoke(authorizeUrl, browserOpened);

        OAuthCallback callback;
        try
        {
            callback = await callbackTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        if (!FixedTimeEquals(callback.State, pkce.State))
        {
            throw new CodexAuthException("The Codex callback state did not match. Start sign-in again.");
        }

        return await ExchangeCodeAsync(callback.Code, pkce.Verifier, cancellationToken);
    }

    internal async Task<CodexTokens> ExchangeCodeAsync(
        string code,
        string verifier,
        CancellationToken cancellationToken)
    {
        return await PostTokenRequestAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = CodexConstants.ClientId,
                ["code"] = code,
                ["redirect_uri"] = CodexConstants.RedirectUri,
                ["code_verifier"] = verifier,
            },
            fallbackRefreshToken: null,
            cancellationToken);
    }

    internal async Task<CodexTokens> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        return await PostTokenRequestAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = CodexConstants.ClientId,
                ["refresh_token"] = refreshToken,
            },
            fallbackRefreshToken: refreshToken,
            cancellationToken);
    }

    private async Task<CodexTokens> PostTokenRequestAsync(
        Dictionary<string, string> form,
        string? fallbackRefreshToken,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(CodexConstants.TokenRequestTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Post, CodexConstants.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.TryAddWithoutValidation("user-agent", CodexConstants.UserAgent);
        request.Headers.TryAddWithoutValidation("accept", "application/json");
        request.Headers.TryAddWithoutValidation("originator", CodexConstants.Originator);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CodexAuthException("The Codex sign-in service timed out. Try again.");
        }
        catch (HttpRequestException exception)
        {
            throw new CodexAuthException("Could not reach the Codex sign-in service. Check your connection.", exception);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(timeoutSource.Token);
            if (!response.IsSuccessStatusCode)
            {
                var errorCode = ReadOAuthErrorCode(body);
                if (response.StatusCode == HttpStatusCode.Unauthorized ||
                    (errorCode is not null && RejectedGrantCodes.Contains(errorCode)))
                {
                    throw new CodexReauthRequiredException(
                        "Codex rejected the stored credentials. Sign in to Codex again.");
                }

                throw new CodexAuthException(
                    $"The Codex sign-in service returned HTTP {(int)response.StatusCode}" +
                    (errorCode is null ? "." : $" ({SafeCode(errorCode)})."));
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (!root.TryGetProperty("access_token", out var accessTokenElement) ||
                    accessTokenElement.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(accessTokenElement.GetString()) ||
                    !root.TryGetProperty("expires_in", out var expiresElement) ||
                    expiresElement.ValueKind != JsonValueKind.Number ||
                    !expiresElement.TryGetDouble(out var expiresInSeconds) ||
                    !double.IsFinite(expiresInSeconds) ||
                    expiresInSeconds <= 0)
                {
                    throw new CodexAuthException("The Codex sign-in response was missing token data. Try again.");
                }

                string? refreshToken = null;
                if (root.TryGetProperty("refresh_token", out var refreshElement) &&
                    refreshElement.ValueKind == JsonValueKind.String)
                {
                    refreshToken = refreshElement.GetString();
                }

                string? scope = null;
                if (root.TryGetProperty("scope", out var scopeElement) &&
                    scopeElement.ValueKind == JsonValueKind.String)
                {
                    scope = scopeElement.GetString();
                }

                var expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(expiresInSeconds, 60 * 60 * 24 * 30));
                return new CodexTokens(
                    accessTokenElement.GetString()!,
                    string.IsNullOrWhiteSpace(refreshToken) ? fallbackRefreshToken : refreshToken,
                    expiresAt,
                    scope);
            }
            catch (JsonException exception)
            {
                throw new CodexAuthException("The Codex sign-in response was not valid JSON. Try again.", exception);
            }
        }
    }

    private static bool TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string? ReadOAuthErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var error = document.RootElement.TryGetProperty("error", out var errorElement)
                ? errorElement
                : default;

            if (error.ValueKind == JsonValueKind.String)
            {
                return SafeCode(error.GetString() ?? string.Empty);
            }

            if (error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("code", out var codeElement) &&
                codeElement.ValueKind == JsonValueKind.String)
            {
                return SafeCode(codeElement.GetString() ?? string.Empty);
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string? SafeCode(string value)
    {
        var safe = new string(value.Where(character =>
            char.IsLetterOrDigit(character) || character is '_' or '-').Take(80).ToArray());
        return safe.Length == 0 ? null : safe;
    }
}

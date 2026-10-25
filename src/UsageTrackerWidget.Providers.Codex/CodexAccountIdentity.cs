using System.Text;
using System.Text.Json;

namespace UsageTrackerWidget.Providers.Codex;

internal static class CodexAccountIdentity
{
    public static string ReadAccountId(string accessToken)
    {
        var segments = accessToken.Split('.');
        if (segments.Length != 3)
        {
            throw new CodexAuthException("The Codex access token is not a JWT. Reconnect Codex.");
        }

        try
        {
            var payload = DecodeBase64Url(segments[1]);
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty(CodexConstants.AccountIdClaim, out var authClaim) &&
                authClaim.ValueKind == JsonValueKind.Object &&
                authClaim.TryGetProperty(CodexConstants.AccountIdProperty, out var accountId) &&
                accountId.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(accountId.GetString()))
            {
                return accountId.GetString()!;
            }
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or InvalidOperationException)
        {
            throw new CodexAuthException("The Codex access token has an unreadable payload. Reconnect Codex.", exception);
        }

        throw new CodexAuthException("The Codex access token does not contain an account ID. Reconnect Codex.");
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Convert.FromBase64String(normalized);
    }
}

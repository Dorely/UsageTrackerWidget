using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UsageTrackerWidget.Providers.Codex;

internal sealed partial class CodexBackendClient
{
    private readonly CodexTokenManager _tokenManager;
    private readonly HttpClient _httpClient;

    public CodexBackendClient(CodexTokenManager tokenManager, HttpClient httpClient)
    {
        _tokenManager = tokenManager;
        _httpClient = httpClient;
    }

    public async Task<HttpResponseMessage> SendAsync(
        Func<CodexCredentials, HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        var credentials = await _tokenManager.GetCredentialsAsync(cancellationToken);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = requestFactory(credentials);
            var response = await _httpClient.SendAsync(request, completionOption, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Unauthorized || attempt == 1)
            {
                return response;
            }

            response.Dispose();
            credentials = await _tokenManager.RecoverFromUnauthorizedAsync(
                credentials.AccessToken,
                cancellationToken);
        }

        throw new UnreachableException();
    }

    public static void ApplyHeaders(HttpRequestMessage request, CodexCredentials credentials)
    {
        request.Headers.TryAddWithoutValidation("authorization", $"Bearer {credentials.AccessToken}");
        request.Headers.TryAddWithoutValidation("chatgpt-account-id", credentials.AccountId);
        request.Headers.TryAddWithoutValidation("originator", CodexConstants.Originator);
        request.Headers.TryAddWithoutValidation("user-agent", CodexConstants.UserAgent);
    }

    public static string DescribeHttpFailure(HttpResponseMessage response, string body)
    {
        var detail = ReadBackendDetail(body);
        var message = $"Codex returned HTTP {(int)response.StatusCode}.";
        if (response.StatusCode == HttpStatusCode.TooManyRequests &&
            response.Headers.RetryAfter?.Delta is { } retryAfter)
        {
            message += $" Retry after {Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))} seconds.";
        }

        if (!string.IsNullOrWhiteSpace(detail))
        {
            message += $" {detail}";
        }

        return message;
    }

    public static string? ReadBackendDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var candidate = root;
            if (root.TryGetProperty("error", out var error))
            {
                candidate = error;
            }
            else if (root.TryGetProperty("detail", out var detail))
            {
                candidate = detail;
            }

            string? value = null;
            if (candidate.ValueKind == JsonValueKind.Object)
            {
                if (candidate.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                {
                    value = code.GetString();
                }
                else if (candidate.TryGetProperty("message", out var message) &&
                         message.ValueKind == JsonValueKind.String)
                {
                    value = message.GetString();
                }
                else if (candidate.TryGetProperty("msg", out var msg) && msg.ValueKind == JsonValueKind.String)
                {
                    value = msg.GetString();
                }
            }
            else if (candidate.ValueKind == JsonValueKind.String)
            {
                value = candidate.GetString();
            }

            return SanitizeDetail(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? ReadBackendErrorType(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("error", out var error) &&
                   error.ValueKind == JsonValueKind.Object &&
                   error.TryGetProperty("type", out var type) &&
                   type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? SanitizeDetail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = WhitespaceRegex().Replace(value, " ");
        sanitized = EmailRegex().Replace(sanitized, "[redacted]");
        sanitized = GuidRegex().Replace(sanitized, "[redacted]");
        sanitized = TokenLikeRegex().Replace(sanitized, "[redacted]");
        sanitized = new string(sanitized
            .Where(character => !char.IsControl(character))
            .Take(240)
            .ToArray())
            .Trim();
        return sanitized.Length == 0 ? null : sanitized;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"[A-Za-z0-9_\-]{40,}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenLikeRegex();
}

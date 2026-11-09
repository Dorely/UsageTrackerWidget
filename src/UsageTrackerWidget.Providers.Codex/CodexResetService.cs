using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace UsageTrackerWidget.Providers.Codex;

public sealed record CodexResetCredit(string Id, DateTimeOffset? ExpiresAt);
public sealed record CodexResetInventory(int AvailableCount, IReadOnlyList<CodexResetCredit> Credits);

public sealed class CodexResetException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
}

// Reset IDs are retained only in memory; other fields in the backend response are discarded.
public sealed class CodexResetService
{
    private const string Endpoint = "https://chatgpt.com/backend-api/wham/rate-limit-reset-credits";
    private readonly CodexBackendClient _backend;

    public CodexResetService(CodexTokenManager tokenManager, HttpClient httpClient)
    {
        _backend = new CodexBackendClient(tokenManager, httpClient);
    }

    public async Task<CodexResetInventory> ReadAsync(CancellationToken cancellationToken)
    {
        using var response = await _backend.SendAsync(credentials =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            CodexBackendClient.ApplyHeaders(request, credentials);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new CodexResetException(CodexBackendClient.DescribeHttpFailure(response, body), (int)response.StatusCode);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("available_count", out var countElement) ||
                !countElement.TryGetInt32(out var count) || count < 0 ||
                !root.TryGetProperty("credits", out var creditsElement) ||
                creditsElement.ValueKind != JsonValueKind.Array)
            {
                throw new CodexResetException("Codex returned an unexpected reset inventory (HTTP 200).");
            }

            var credits = new List<CodexResetCredit>();
            foreach (var item in creditsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(idElement.GetString()) ||
                    !item.TryGetProperty("status", out var statusElement) || statusElement.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("reset_type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
                {
                    throw new CodexResetException("Codex returned incomplete reset details (HTTP 200).");
                }

                if (statusElement.GetString() != "available" || typeElement.GetString() != "codex_rate_limits")
                {
                    continue;
                }

                DateTimeOffset? expiry = null;
                if (item.TryGetProperty("expires_at", out var expiryElement) &&
                    expiryElement.ValueKind == JsonValueKind.String &&
                    HasTimeZone(expiryElement.GetString()) &&
                    DateTimeOffset.TryParse(expiryElement.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    expiry = parsed.ToUniversalTime();
                }

                credits.Add(new CodexResetCredit(idElement.GetString()!, expiry));
            }

            if (credits.Select(credit => credit.Id).Distinct(StringComparer.Ordinal).Count() != credits.Count ||
                credits.Count > count)
            {
                throw new CodexResetException("Codex returned inconsistent reset details (HTTP 200).");
            }

            return new CodexResetInventory(count, credits);
        }
        catch (JsonException)
        {
            throw new CodexResetException("Codex returned invalid reset inventory JSON (HTTP 200).");
        }
        catch (InvalidOperationException)
        {
            throw new CodexResetException("Codex returned an unexpected reset inventory (HTTP 200).");
        }
    }

    private static bool HasTimeZone(string? value)
    {
        if (value is null) return false;
        var separator = value.IndexOf('T');
        return separator >= 0 && (value.EndsWith('Z') || value.LastIndexOf('+') > separator ||
            value.LastIndexOf('-') > separator);
    }

    public async Task<string> ConsumeAsync(string creditId, string requestId, CancellationToken cancellationToken)
    {
        using var response = await _backend.SendAsync(credentials =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, Endpoint + "/consume");
            CodexBackendClient.ApplyHeaders(request, credentials);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                redeem_request_id = requestId,
                credit_id = creditId,
            }), Encoding.UTF8, "application/json");
            return request;
        }, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new CodexResetException(CodexBackendClient.DescribeHttpFailure(response, body), (int)response.StatusCode);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("code", out var code) &&
                code.ValueKind == JsonValueKind.String &&
                code.GetString() is "reset" or "already_redeemed" or "nothing_to_reset" or "no_credit")
            {
                return code.GetString()!;
            }
        }
        catch (JsonException)
        {
        }

        throw new CodexResetException("Codex returned an unrecognized reset result (HTTP 200).");
    }
}

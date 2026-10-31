using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UsageTrackerWidget.Core;

namespace UsageTrackerWidget.Providers.Codex;

public sealed class CodexUsageProvider : IUsageProvider
{
    private const string RateLimitSourceKey = "codex.rate-limits";
    private const string RateLimitSourceName = "Codex rate limits";
    private const string ImageSourceKey = "codex.image-meter";
    private const string ImageSourceName = "Daily image allowance";

    private readonly CodexBackendClient _backend;
    private volatile bool _imageMeterEnabled;

    public CodexUsageProvider(
        CodexTokenManager tokenManager,
        HttpClient httpClient,
        bool imageMeterEnabled = true)
    {
        _backend = new CodexBackendClient(tokenManager, httpClient);
        _imageMeterEnabled = imageMeterEnabled;
    }

    public bool ImageMeterEnabled
    {
        get => _imageMeterEnabled;
        set => _imageMeterEnabled = value;
    }

    public async Task<UsageReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        var windows = new List<UsageWindow>();
        var errors = new List<UsageSourceError>();
        string? planLabel = null;

        try
        {
            var rateLimits = await ReadRateLimitsAsync(cancellationToken);
            windows.AddRange(rateLimits.Windows);
            errors.AddRange(rateLimits.Errors);
            planLabel = rateLimits.PlanLabel;
        }
        catch (CodexReauthRequiredException exception)
        {
            throw new UsageAuthenticationRequiredException(exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CodexUsageSourceException exception)
        {
            errors.Add(CreateError(RateLimitSourceKey, RateLimitSourceName, exception));
        }
        catch (HttpRequestException)
        {
            errors.Add(CreateError(
                RateLimitSourceKey,
                RateLimitSourceName,
                UsageErrorKind.Network,
                null,
                "Could not reach Codex. Check the network connection."));
        }
        catch (OperationCanceledException)
        {
            errors.Add(CreateError(
                RateLimitSourceKey,
                RateLimitSourceName,
                UsageErrorKind.Network,
                null,
                "The Codex rate-limit request timed out."));
        }

        if (_imageMeterEnabled)
        {
            try
            {
                var imageMeter = await ReadImageMeterAsync(cancellationToken);
                windows.Add(imageMeter.Window);
                planLabel ??= imageMeter.PlanLabel;
            }
            catch (CodexReauthRequiredException exception)
            {
                throw new UsageAuthenticationRequiredException(exception.Message);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (CodexUsageSourceException exception)
            {
                errors.Add(CreateError(ImageSourceKey, ImageSourceName, exception));
            }
            catch (HttpRequestException)
            {
                errors.Add(CreateError(
                    ImageSourceKey,
                    ImageSourceName,
                    UsageErrorKind.Network,
                    null,
                    "Could not reach the Codex image meter. Check the network connection."));
            }
            catch (OperationCanceledException)
            {
                errors.Add(CreateError(
                    ImageSourceKey,
                    ImageSourceName,
                    UsageErrorKind.Network,
                    null,
                    "The Codex image-meter request timed out."));
            }
        }

        var snapshot = new UsageSnapshot(
            CodexConstants.ProviderKey,
            CodexConstants.ProviderDisplayName,
            CodexConstants.AccountLabel,
            planLabel,
            DateTimeOffset.UtcNow,
            windows);

        return new UsageReadResult(snapshot, errors);
    }

    private async Task<ParsedRateLimits> ReadRateLimitsAsync(CancellationToken cancellationToken)
    {
        using var response = await _backend.SendAsync(
            credentials =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, CodexConstants.UsageEndpoint);
                CodexBackendClient.ApplyHeaders(request, credentials);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return request;
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateHttpSourceExceptionAsync(response, cancellationToken);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            return ParseRateLimits(document.RootElement);
        }
        catch (JsonException)
        {
            throw new CodexUsageSourceException(
                "Codex returned a rate-limit response that was not valid JSON.",
                UsageErrorKind.InvalidResponse,
                (int)response.StatusCode);
        }
    }

    private async Task<ParsedImageMeter> ReadImageMeterAsync(CancellationToken cancellationToken)
    {
        using var response = await _backend.SendAsync(
            credentials =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, CodexConstants.ImagesEndpoint);
                CodexBackendClient.ApplyHeaders(request, credentials);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new { model = CodexConstants.ImageMeterModel }),
                    Encoding.UTF8,
                    "application/json");
                return request;
            },
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var expectedRejection = response.StatusCode == HttpStatusCode.BadRequest &&
            (body.Contains("missing_required_parameter", StringComparison.OrdinalIgnoreCase) ||
             (body.Contains("\"param\"", StringComparison.OrdinalIgnoreCase) &&
              body.Contains("prompt", StringComparison.OrdinalIgnoreCase)));

        // Once the allowance is exhausted the backend rejects the probe with 429 before validating it,
        // and still sends the meter headers.
        var limitReachedRejection = response.StatusCode == HttpStatusCode.TooManyRequests &&
            string.Equals(CodexBackendClient.ReadBackendErrorType(body), "usage_limit_reached", StringComparison.Ordinal);

        if (!expectedRejection && !limitReachedRejection)
        {
            var isTransientOrAuthFailure = response.StatusCode is
                HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden or
                HttpStatusCode.TooManyRequests ||
                (int)response.StatusCode >= 500;

            if (isTransientOrAuthFailure)
            {
                var transientDetail = CreateHttpSourceException(response, body);
                throw new CodexUsageSourceException(
                    transientDetail.Message,
                    response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                        ? UsageErrorKind.Authentication
                        : UsageErrorKind.Http,
                    (int)response.StatusCode);
            }

            _imageMeterEnabled = false;
            var detail = CreateHttpSourceException(response, body);
            throw new CodexUsageSourceException(
                $"The image-meter probe returned an unexpected response (HTTP {(int)response.StatusCode}). " +
                "Automatic image tracking was disabled. " +
                detail.Message,
                UsageErrorKind.ContractChanged,
                (int)response.StatusCode);
        }

        if (!response.Headers.TryGetValues("x-codex-primary-used-percent", out var usedValues))
        {
            _imageMeterEnabled = false;
            throw new CodexUsageSourceException(
                "The image-meter response no longer includes the used-percent header. " +
                "Automatic image tracking was disabled.",
                UsageErrorKind.ContractChanged,
                (int)response.StatusCode);
        }

        var rawUsed = usedValues.FirstOrDefault();
        if (!TryParsePercent(rawUsed, out var usedPercent))
        {
            _imageMeterEnabled = false;
            throw new CodexUsageSourceException(
                "The image-meter used-percent header was invalid. Automatic image tracking was disabled.",
                UsageErrorKind.ContractChanged,
                (int)response.StatusCode);
        }

        var windowLength = ReadWindowLength(response, "x-codex-primary-window-minutes");
        var resetAt = ReadResetAt(response);
        var planLabel = ReadPlanLabel(response);
        var window = new UsageWindow(
            UsageWindowKeys.CodexDailyImages,
            "Daily images",
            usedPercent,
            windowLength,
            resetAt,
            limitReachedRejection || usedPercent >= 100);

        return new ParsedImageMeter(window, planLabel);
    }

    private static ParsedRateLimits ParseRateLimits(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("rate_limit", out var rateLimit) ||
            rateLimit.ValueKind != JsonValueKind.Object)
        {
            throw new CodexUsageSourceException(
                "Codex returned an unexpected rate-limit shape.",
                UsageErrorKind.InvalidResponse);
        }

        var windows = new List<UsageWindow>();
        var errors = new List<UsageSourceError>();
        var observedAt = DateTimeOffset.UtcNow;
        var planLabel = ReadPlanLabel(root);

        AddRateWindow(
            rateLimit,
            "primary_window",
            UsageWindowKeys.CodexSession,
            "5-hour",
            windows,
            errors,
            observedAt);
        AddRateWindow(
            rateLimit,
            "secondary_window",
            UsageWindowKeys.CodexWeekly,
            "Weekly",
            windows,
            errors,
            observedAt);

        if (!rateLimit.TryGetProperty("primary_window", out _))
        {
            errors.Add(new UsageSourceError(
                RateLimitSourceKey,
                RateLimitSourceName,
                UsageErrorKind.InvalidResponse,
                null,
                "The 5-hour usage window was missing, so it is shown as unknown.",
                observedAt));
        }

        if (!rateLimit.TryGetProperty("secondary_window", out _))
        {
            errors.Add(new UsageSourceError(
                RateLimitSourceKey,
                RateLimitSourceName,
                UsageErrorKind.InvalidResponse,
                null,
                "The weekly usage window was missing, so it is shown as unknown.",
                observedAt));
        }

        if (windows.Count == 0)
        {
            throw new CodexUsageSourceException(
                "Codex returned no recognizable usage windows.",
                UsageErrorKind.InvalidResponse);
        }

        return new ParsedRateLimits(windows, errors, planLabel);
    }

    private static void AddRateWindow(
        JsonElement rateLimit,
        string propertyName,
        string key,
        string displayName,
        List<UsageWindow> windows,
        List<UsageSourceError> errors,
        DateTimeOffset observedAt)
    {
        if (!rateLimit.TryGetProperty(propertyName, out var windowElement) ||
            windowElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        double? usedPercent = null;
        if (windowElement.TryGetProperty("used_percent", out var usedElement) &&
            usedElement.ValueKind == JsonValueKind.Number &&
            usedElement.TryGetDouble(out var parsedUsed) &&
            double.IsFinite(parsedUsed) &&
            parsedUsed is >= 0 and <= 100)
        {
            usedPercent = parsedUsed;
        }
        else
        {
            errors.Add(new UsageSourceError(
                RateLimitSourceKey,
                RateLimitSourceName,
                UsageErrorKind.InvalidResponse,
                null,
                $"{displayName} usage was missing or invalid, so it is shown as unknown.",
                observedAt));
        }

        TimeSpan? windowLength = null;
        if (windowElement.TryGetProperty("limit_window_seconds", out var windowElementSeconds) &&
            windowElementSeconds.ValueKind == JsonValueKind.Number &&
            windowElementSeconds.TryGetDouble(out var seconds) &&
            double.IsFinite(seconds) &&
            seconds > 0 &&
            seconds <= TimeSpan.MaxValue.TotalSeconds)
        {
            windowLength = TimeSpan.FromSeconds(seconds);
        }

        DateTimeOffset? resetAt = null;
        if (windowElement.TryGetProperty("reset_at", out var resetElement) &&
            resetElement.ValueKind == JsonValueKind.Number &&
            resetElement.TryGetInt64(out var epochSeconds) &&
            epochSeconds > 0)
        {
            try
            {
                resetAt = DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        var isLimitReached = usedPercent >= 100 ||
            (rateLimit.TryGetProperty("limit_reached", out var limitElement) &&
             limitElement.ValueKind == JsonValueKind.True);

        windows.Add(new UsageWindow(
            key,
            displayName,
            usedPercent,
            windowLength,
            resetAt,
            isLimitReached));
    }

    private static TimeSpan? ReadWindowLength(HttpResponseMessage response, string headerName)
    {
        if (!response.Headers.TryGetValues(headerName, out var values) ||
            !double.TryParse(
                values.FirstOrDefault(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var minutes) ||
            !double.IsFinite(minutes) ||
            minutes <= 0 ||
            minutes > TimeSpan.MaxValue.TotalMinutes)
        {
            return null;
        }

        return TimeSpan.FromMinutes(minutes);
    }

    private static DateTimeOffset? ReadResetAt(HttpResponseMessage response)
    {
        long? epochSeconds = null;
        if (response.Headers.TryGetValues("x-codex-primary-reset-at", out var resetValues) &&
            long.TryParse(
                resetValues.FirstOrDefault(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedReset))
        {
            epochSeconds = parsedReset;
        }
        else if (response.Headers.TryGetValues("x-codex-primary-reset-after-seconds", out var afterValues) &&
                 long.TryParse(
                     afterValues.FirstOrDefault(),
                     NumberStyles.Integer,
                     CultureInfo.InvariantCulture,
                     out var resetAfterSeconds) &&
                 resetAfterSeconds > 0)
        {
            return DateTimeOffset.UtcNow.AddSeconds(resetAfterSeconds);
        }

        if (epochSeconds is null or <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(epochSeconds.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? ReadPlanLabel(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("x-codex-plan-type", out var values)
            ? SanitizePlanLabel(values.FirstOrDefault())
            : null;
    }

    private static string? ReadPlanLabel(JsonElement root)
    {
        return root.TryGetProperty("plan_type", out var planElement) &&
               planElement.ValueKind == JsonValueKind.String
            ? SanitizePlanLabel(planElement.GetString())
            : null;
    }

    private static string? SanitizePlanLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var safe = new string(value
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-')
            .Take(32)
            .ToArray())
            .Trim('.', '_', '-');
        return safe.Length == 0 ? null : safe;
    }

    private static bool TryParsePercent(string? value, out double percent)
    {
        percent = 0;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed) ||
            parsed is < 0 or > 100)
        {
            return false;
        }

        percent = parsed;
        return true;
    }

    private static async Task<CodexUsageSourceException> CreateHttpSourceExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return CreateHttpSourceException(response, body);
    }

    private static CodexUsageSourceException CreateHttpSourceException(HttpResponseMessage response, string body)
    {
        return new CodexUsageSourceException(
            CodexBackendClient.DescribeHttpFailure(response, body),
            UsageErrorKind.Http,
            (int)response.StatusCode);
    }

    private static UsageSourceError CreateError(
        string sourceKey,
        string displayName,
        CodexUsageSourceException exception)
    {
        return CreateError(
            sourceKey,
            displayName,
            exception.Kind,
            exception.StatusCode,
            exception.Message);
    }

    private static UsageSourceError CreateError(
        string sourceKey,
        string displayName,
        UsageErrorKind kind,
        int? statusCode,
        string message)
    {
        return new UsageSourceError(
            sourceKey,
            displayName,
            kind,
            statusCode,
            message,
            DateTimeOffset.UtcNow);
    }

    private sealed record ParsedRateLimits(
        IReadOnlyList<UsageWindow> Windows,
        IReadOnlyList<UsageSourceError> Errors,
        string? PlanLabel);

    private sealed record ParsedImageMeter(UsageWindow Window, string? PlanLabel);
}

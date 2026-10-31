using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UsageTrackerWidget.Core;

namespace UsageTrackerWidget.Providers.Codex;

// Sends the smallest metered request for a window so its countdown begins. Responses are
// drained and discarded; nothing generated is kept.
public sealed class CodexWindowStarter : IUsageWindowStarter
{
    private const string SessionPrompt = "Reply with the single word OK.";
    private const string ImagePrompt = "A single small gray dot centered on a plain white background.";

    private readonly CodexBackendClient _backend;

    public CodexWindowStarter(CodexTokenManager tokenManager, HttpClient httpClient)
    {
        _backend = new CodexBackendClient(tokenManager, httpClient);
    }

    public bool CanStart(string windowKey)
    {
        return windowKey is UsageWindowKeys.CodexSession or UsageWindowKeys.CodexDailyImages;
    }

    public async Task StartAsync(string windowKey, CancellationToken cancellationToken)
    {
        try
        {
            switch (windowKey)
            {
                case UsageWindowKeys.CodexSession:
                    await StartSessionAsync(cancellationToken);
                    break;
                case UsageWindowKeys.CodexDailyImages:
                    await StartImagesAsync(cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(windowKey), windowKey, "Window cannot be started.");
            }
        }
        catch (CodexReauthRequiredException exception)
        {
            throw new UsageAuthenticationRequiredException(exception.Message);
        }
        catch (HttpRequestException)
        {
            throw new UsageWindowStartException("Could not reach Codex. Check the network connection.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UsageWindowStartException("The Codex request timed out.");
        }
        catch (IOException)
        {
            throw new UsageWindowStartException("The Codex response stream ended unexpectedly.");
        }
    }

    private async Task StartSessionAsync(CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = CodexConstants.SessionKickoffModel,
            instructions = SessionPrompt,
            input = new[]
            {
                new
                {
                    role = "user",
                    content = new[] { new { type = "input_text", text = "OK" } },
                },
            },
            reasoning = new { effort = CodexConstants.SessionKickoffEffort },
            stream = true,
            store = false,
        });

        using var response = await _backend.SendAsync(
            credentials =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, CodexConstants.ResponsesEndpoint);
                CodexBackendClient.ApplyHeaders(request, credentials);
                request.Headers.TryAddWithoutValidation("OpenAI-Beta", "responses=experimental");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                return request;
            },
            cancellationToken,
            HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new UsageWindowStartException(
                CodexBackendClient.DescribeHttpFailure(response, errorBody),
                (int)response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var outcome = ReadStreamOutcome(line[5..].Trim());
            if (outcome is null)
            {
                continue;
            }

            if (outcome.Succeeded)
            {
                return;
            }

            throw new UsageWindowStartException(
                outcome.Detail is null
                    ? "Codex reported that the request failed."
                    : $"Codex reported that the request failed. {outcome.Detail}",
                (int)response.StatusCode);
        }

        throw new UsageWindowStartException(
            "The Codex response ended before it completed.",
            (int)response.StatusCode);
    }

    private async Task StartImagesAsync(CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            prompt = ImagePrompt,
            model = CodexConstants.ImageMeterModel,
            size = CodexConstants.ImageKickoffSize,
            quality = CodexConstants.ImageKickoffQuality,
            background = "opaque",
        });

        using var response = await _backend.SendAsync(
            credentials =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, CodexConstants.ImagesEndpoint);
                CodexBackendClient.ApplyHeaders(request, credentials);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                return request;
            },
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new UsageWindowStartException(
                CodexBackendClient.DescribeHttpFailure(response, responseBody),
                (int)response.StatusCode);
        }

        if (!HasImageData(responseBody))
        {
            throw new UsageWindowStartException(
                "Codex accepted the image request but returned no image.",
                (int)response.StatusCode);
        }
    }

    private static StreamOutcome? ReadStreamOutcome(string data)
    {
        if (data.Length == 0 || data == "[DONE]")
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return typeElement.GetString() switch
            {
                "response.completed" or "response.done" or "response.incomplete" => new StreamOutcome(true, null),
                "response.failed" or "error" => new StreamOutcome(false, CodexBackendClient.ReadBackendDetail(
                    root.TryGetProperty("response", out var failed) ? failed.GetRawText() : data)),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasImageData(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Array &&
                data.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record StreamOutcome(bool Succeeded, string? Detail);
}

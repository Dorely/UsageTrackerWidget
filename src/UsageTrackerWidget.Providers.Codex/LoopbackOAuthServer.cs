using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UsageTrackerWidget.Providers.Codex;

internal sealed record OAuthCallback(string Code, string State);

internal static class LoopbackOAuthServer
{
    private const int MaximumRequestBytes = 16 * 1024;

    public static async Task<OAuthCallback> WaitAsync(
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        TcpListener listener;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
        }
        catch (SocketException exception)
        {
            throw new CodexAuthException(
                $"Port {port} is unavailable. Close the process using it and try again.",
                exception);
        }

        try
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(timeoutSource.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new CodexAuthException("Codex sign-in timed out. Start sign-in again when ready.");
                }

                using (client)
                {
                    var request = await ReadRequestAsync(client, timeoutSource.Token);
                    if (request is null)
                    {
                        continue;
                    }

                    var requestLine = request.Split("\r\n", 2, StringSplitOptions.None)[0];
                    var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.Ordinal))
                    {
                        await WriteResponseAsync(client, 400, "Invalid request", "The sign-in callback was invalid.");
                        continue;
                    }

                    if (!Uri.TryCreate($"http://localhost{parts[1]}", UriKind.Absolute, out var callbackUri))
                    {
                        await WriteResponseAsync(client, 400, "Invalid callback", "The sign-in callback was invalid.");
                        continue;
                    }

                    if (!string.Equals(callbackUri.AbsolutePath, CodexConstants.CallbackPath, StringComparison.Ordinal))
                    {
                        await WriteResponseAsync(client, 404, "Not found", "This page is not part of the sign-in flow.");
                        continue;
                    }

                    var query = ParseQuery(callbackUri.Query);
                    if (query.TryGetValue("error", out var oauthError))
                    {
                        await WriteResponseAsync(
                            client,
                            400,
                            "Authorization failed",
                            "Codex did not authorize this app. You can close this tab.");
                        throw new CodexAuthException($"Codex authorization was declined ({SafeCode(oauthError)}).");
                    }

                    if (!query.TryGetValue("code", out var code) ||
                        !query.TryGetValue("state", out var state) ||
                        string.IsNullOrWhiteSpace(code) ||
                        string.IsNullOrWhiteSpace(state))
                    {
                        await WriteResponseAsync(
                            client,
                            400,
                            "Authorization failed",
                            "The callback did not include an authorization code.");
                        throw new CodexAuthException("The Codex callback was missing its authorization code.");
                    }

                    await WriteResponseAsync(
                        client,
                        200,
                        "Codex connected",
                        "Usage Tracker can now read your Codex quota. You can close this tab.");
                    return new OAuthCallback(code, state);
                }
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string?> ReadRequestAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var stream = client.GetStream();
        var buffer = new byte[MaximumRequestBytes];
        var length = 0;

        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            length += read;
            if (buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8) >= 0)
            {
                return Encoding.ASCII.GetString(buffer, 0, length);
            }
        }

        return length == 0 ? null : Encoding.ASCII.GetString(buffer, 0, length);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var component in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = component.IndexOf('=');
            var key = separator >= 0 ? component[..separator] : component;
            var value = separator >= 0 ? component[(separator + 1)..] : string.Empty;
            values[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value);
        }

        return values;
    }

    private static string SafeCode(string value)
    {
        var safe = new string(value.Where(char.IsLetterOrDigit).Take(40).ToArray());
        return safe.Length == 0 ? "unknown error" : safe;
    }

    private static async Task WriteResponseAsync(
        TcpClient client,
        int statusCode,
        string title,
        string message)
    {
        var reason = statusCode switch
        {
            200 => "OK",
            404 => "Not Found",
            _ => "Bad Request",
        };
        var html =
            "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
            $"<title>{title}</title><style>" +
            ":root{color-scheme:dark light}body{margin:0;min-height:100vh;display:grid;place-items:center;" +
            "font-family:'Segoe UI Variable','Segoe UI',sans-serif;background:#10141b;color:#f4f7fb}" +
            ".card{max-width:440px;margin:24px;padding:36px;border:1px solid #303846;border-radius:20px;" +
            "background:#1b1f27;box-shadow:0 20px 60px rgba(0,0,0,.35)}.mark{width:44px;height:44px;" +
            "display:grid;place-items:center;border-radius:14px;background:#dbeafe;color:#1d4ed8;" +
            "font-weight:700;font-size:20px;margin-bottom:22px}h1{margin:0 0 10px;font-size:26px}" +
            "p{margin:0;color:#a9b4c3;line-height:1.55}</style></head><body><main class=\"card\">" +
            "<div class=\"mark\">U</div>" +
            $"<h1>{WebUtility.HtmlEncode(title)}</h1><p>{WebUtility.HtmlEncode(message)}</p>" +
            "</main></body></html>";
        var body = Encoding.UTF8.GetBytes(html);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {reason}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n");

        var stream = client.GetStream();
        await stream.WriteAsync(header);
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }
}

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UsageTrackerWidget.Providers.Codex;

internal sealed record OAuthCallback(string Code, string State);

internal sealed class LoopbackOAuthServer : IDisposable
{
    private const int MaximumRequestBytes = 16 * 1024;

    private readonly TcpListener _listener;

    public LoopbackOAuthServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            _listener.Start();
        }
        catch (SocketException exception)
        {
            throw new CodexAuthException(
                "The local sign-in callback listener could not start. Check the Windows firewall and try again.",
                exception);
        }

        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        RedirectUri = new Uri(
            $"http://{CodexConstants.CallbackHost}:{port}{CodexConstants.CallbackPath}");
    }

    public Uri RedirectUri { get; }

    public async Task<OAuthCallback> WaitAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(timeoutSource.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw CreateTimeoutException();
                }
                catch (ObjectDisposedException)
                {
                    throw new CodexAuthException("Codex sign-in was canceled.");
                }
                catch (Exception exception) when (exception is SocketException or IOException)
                {
                    if (timeoutSource.IsCancellationRequested)
                    {
                        throw CreateTimeoutException();
                    }

                    continue;
                }

                using (client)
                {
                    try
                    {
                        var callback = await HandleClientAsync(client, timeoutSource.Token);
                        if (callback is not null)
                        {
                            return callback;
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (CodexAuthException)
                    {
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        throw CreateTimeoutException();
                    }
                    catch (Exception)
                    {
                        if (timeoutSource.IsCancellationRequested)
                        {
                            throw CreateTimeoutException();
                        }
                    }
                }
            }
        }
        finally
        {
            _listener.Stop();
        }
    }

    private static async Task<OAuthCallback?> HandleClientAsync(
        TcpClient client,
        CancellationToken cancellationToken)
    {
        var request = await ReadRequestAsync(client, cancellationToken);
        if (request is null)
        {
            return null;
        }

        var requestLine = request.Split("\r\n", 2, StringSplitOptions.None)[0];
        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.Ordinal))
        {
            await TryWriteResponseAsync(client, 400, "Invalid request", "The sign-in callback was invalid.");
            return null;
        }

        if (!Uri.TryCreate($"http://{CodexConstants.CallbackHost}{parts[1]}", UriKind.Absolute, out var callbackUri))
        {
            await TryWriteResponseAsync(client, 400, "Invalid callback", "The sign-in callback was invalid.");
            return null;
        }

        if (!string.Equals(callbackUri.AbsolutePath, CodexConstants.CallbackPath, StringComparison.Ordinal))
        {
            await TryWriteResponseAsync(client, 404, "Not found", "This page is not part of the sign-in flow.");
            return null;
        }

        var query = ParseQuery(callbackUri.Query);
        if (query.TryGetValue("error", out var oauthError))
        {
            await TryWriteResponseAsync(
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
            await TryWriteResponseAsync(
                client,
                400,
                "Authorization failed",
                "The callback did not include an authorization code.");
            throw new CodexAuthException("The Codex callback was missing its authorization code.");
        }

        var callback = new OAuthCallback(code, state);

        // The browser may drop the connection immediately after sending the
        // callback. Do not discard a valid authorization code because the
        // completion page could not be written.
        await TryWriteResponseAsync(
            client,
            200,
            "Codex connected",
            "Usage Tracker can now read your Codex quota. You can close this tab.");
        return callback;
    }

    private static async Task<string?> ReadRequestAsync(TcpClient client, CancellationToken cancellationToken)
    {
        // The caller owns the client and its stream for the full request/response.
        var stream = client.GetStream();
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

    private static Task TryWriteResponseAsync(
        TcpClient client,
        int statusCode,
        string title,
        string message)
    {
        return WriteResponseAsync(client, statusCode, title, message);
    }

    private static async Task WriteResponseAsync(
        TcpClient client,
        int statusCode,
        string title,
        string message)
    {
        try
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
        catch (Exception)
        {
        }
    }

    private static CodexAuthException CreateTimeoutException()
    {
        return new CodexAuthException("Codex sign-in timed out. Start sign-in again when ready.");
    }

    public void Dispose()
    {
        _listener.Stop();
    }
}

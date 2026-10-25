using System.Security.Cryptography;
using System.Text.Json;

namespace UsageTrackerWidget.Providers.Codex;

internal interface ICodexTokenStore
{
    Task<StoredCodexTokens?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(StoredCodexTokens tokens, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

internal sealed class DpapiCodexTokenStore : ICodexTokenStore
{
    private static readonly byte[] Entropy = "UsageTrackerWidget.Codex.Credentials.v1"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _path;

    public DpapiCodexTokenStore()
        : this(DefaultPath())
    {
    }

    internal DpapiCodexTokenStore(string path)
    {
        _path = path;
    }

    public static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "UsageTrackerWidget", "credentials", "codex.dat");
    }

    async Task<StoredCodexTokens?> ICodexTokenStore.LoadAsync(CancellationToken cancellationToken)
    {
        byte[] protectedBytes;
        try
        {
            protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        byte[] plainBytes;
        try
        {
            plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException exception)
        {
            throw new CodexAuthException(
                "Stored Codex credentials could not be decrypted for this Windows user. Reconnect Codex.",
                exception);
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredCodexTokens>(plainBytes, JsonOptions);
            if (stored is null ||
                string.IsNullOrWhiteSpace(stored.AccessToken) ||
                stored.ExpiresAt == default)
            {
                throw new CodexAuthException("Stored Codex credentials are incomplete. Reconnect Codex.");
            }

            return stored;
        }
        catch (JsonException exception)
        {
            throw new CodexAuthException("Stored Codex credentials are invalid. Reconnect Codex.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    async Task ICodexTokenStore.SaveAsync(StoredCodexTokens tokens, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The credential directory is unavailable.");
        Directory.CreateDirectory(directory);

        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(tokens, JsonOptions);
        byte[] encryptedBytes;
        try
        {
            encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }

        var temporaryPath = Path.Combine(
            directory,
            $".codex-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, encryptedBytes, cancellationToken);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    Task ICodexTokenStore.ClearAsync(CancellationToken cancellationToken)
    {
        File.Delete(_path);
        return Task.CompletedTask;
    }
}

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UsageTrackerWidget.App.Storage;

internal sealed record ResetAttempt(string CreditHash, string RequestId, DateTimeOffset ExpiresAt, string Outcome);

// The journal contains no credential, account ID, or backend credit ID.
internal sealed class ResetAttemptStore
{
    private readonly string _path;
    private readonly List<ResetAttempt> _attempts;

    public ResetAttemptStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.RootDirectory, "reset-attempts.json");
        if (!File.Exists(_path))
        {
            _attempts = [];
            return;
        }

        _attempts = JsonSerializer.Deserialize<List<ResetAttempt>>(File.ReadAllText(_path))
            ?? throw new InvalidDataException("The reset attempt journal is invalid.");
        if (_attempts.Any(a => a is null || string.IsNullOrWhiteSpace(a.CreditHash) ||
            !Guid.TryParse(a.RequestId, out _) || string.IsNullOrWhiteSpace(a.Outcome)) ||
            _attempts.Select(a => a.CreditHash).Distinct(StringComparer.Ordinal).Count() != _attempts.Count)
        {
            throw new InvalidDataException("The reset attempt journal is invalid.");
        }
    }

    public static string Hash(string creditId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(creditId)));

    public ResetAttempt? Find(string hash) => _attempts.FirstOrDefault(attempt => attempt.CreditHash == hash);

    public ResetAttempt? LastAttempt => _attempts.LastOrDefault();

    public IEnumerable<ResetAttempt> Pending => _attempts.Where(attempt => attempt.Outcome == "pending");

    public ResetAttempt Prepare(string hash, DateTimeOffset expiresAt)
    {
        if (Find(hash) is { } existing)
        {
            return existing;
        }

        var attempt = new ResetAttempt(hash, Guid.NewGuid().ToString(), expiresAt, "pending");
        Save(_attempts.Append(attempt).ToList());
        _attempts.Add(attempt);
        return attempt;
    }

    public void Complete(ResetAttempt attempt, string outcome)
    {
        var updated = _attempts.Select(item => item.CreditHash == attempt.CreditHash
            ? item with { Outcome = outcome }
            : item).ToList();
        Save(updated);
        _attempts.Clear();
        _attempts.AddRange(updated);
    }

    private void Save(List<ResetAttempt> attempts)
    {
        AppPaths.EnsureRootDirectory();
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(attempts));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}

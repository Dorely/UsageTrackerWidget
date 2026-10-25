using Microsoft.Data.Sqlite;
using UsageTrackerWidget.Core;

namespace UsageTrackerWidget.App.Storage;

internal sealed class UsageHistoryRepository
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public UsageHistoryRepository(string? path = null)
    {
        var dataSource = path ?? AppPaths.HistoryFile;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            AppPaths.EnsureRootDirectory();
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;

                CREATE TABLE IF NOT EXISTS usage_samples (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    provider_key TEXT NOT NULL,
                    account_label TEXT NOT NULL,
                    plan_label TEXT NULL,
                    window_key TEXT NOT NULL,
                    window_name TEXT NOT NULL,
                    observed_at INTEGER NOT NULL,
                    used_percent REAL NULL,
                    window_seconds INTEGER NULL,
                    reset_at INTEGER NULL,
                    is_limit_reached INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_usage_samples_window_observed
                    ON usage_samples(provider_key, window_key, observed_at);

                PRAGMA user_version = 1;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        UsageSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.Windows.Count == 0)
        {
            return;
        }

        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText =
                """
                INSERT INTO usage_samples (
                    provider_key,
                    account_label,
                    plan_label,
                    window_key,
                    window_name,
                    observed_at,
                    used_percent,
                    window_seconds,
                    reset_at,
                    is_limit_reached
                )
                VALUES (
                    $provider_key,
                    $account_label,
                    $plan_label,
                    $window_key,
                    $window_name,
                    $observed_at,
                    $used_percent,
                    $window_seconds,
                    $reset_at,
                    $is_limit_reached
                );
                """;

            var providerKey = command.Parameters.Add("$provider_key", SqliteType.Text);
            var accountLabel = command.Parameters.Add("$account_label", SqliteType.Text);
            var planLabel = command.Parameters.Add("$plan_label", SqliteType.Text);
            var windowKey = command.Parameters.Add("$window_key", SqliteType.Text);
            var windowName = command.Parameters.Add("$window_name", SqliteType.Text);
            var observedAt = command.Parameters.Add("$observed_at", SqliteType.Integer);
            var usedPercent = command.Parameters.Add("$used_percent", SqliteType.Real);
            var windowSeconds = command.Parameters.Add("$window_seconds", SqliteType.Integer);
            var resetAt = command.Parameters.Add("$reset_at", SqliteType.Integer);
            var isLimitReached = command.Parameters.Add("$is_limit_reached", SqliteType.Integer);

            foreach (var window in snapshot.Windows)
            {
                providerKey.Value = snapshot.ProviderKey;
                accountLabel.Value = snapshot.AccountLabel;
                planLabel.Value = (object?)snapshot.PlanLabel ?? DBNull.Value;
                windowKey.Value = window.Key;
                windowName.Value = window.DisplayName;
                observedAt.Value = snapshot.ObservedAt.ToUnixTimeSeconds();
                usedPercent.Value = window.UsedPercent is { } used ? used : DBNull.Value;
                windowSeconds.Value = window.WindowLength is { } length
                    ? (long)Math.Round(length.TotalSeconds)
                    : DBNull.Value;
                resetAt.Value = window.ResetsAt?.ToUnixTimeSeconds() ?? (object)DBNull.Value;
                isLimitReached.Value = window.IsLimitReached ? 1 : 0;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, LatestUsageSample>> LoadLatestAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    provider_key,
                    window_key,
                    window_name,
                    used_percent,
                    window_seconds,
                    reset_at,
                    is_limit_reached,
                    observed_at,
                    plan_label
                FROM (
                    SELECT
                        provider_key,
                        window_key,
                        window_name,
                        used_percent,
                        window_seconds,
                        reset_at,
                        is_limit_reached,
                        observed_at,
                        plan_label,
                        ROW_NUMBER() OVER (
                            PARTITION BY provider_key, window_key
                            ORDER BY observed_at DESC, id DESC
                        ) AS row_number
                    FROM usage_samples
                )
                WHERE row_number = 1;
                """;

            var result = new Dictionary<string, LatestUsageSample>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var windowKey = reader.GetString(1);
                result[windowKey] = new LatestUsageSample(
                    reader.GetString(0),
                    windowKey,
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    reader.IsDBNull(4) ? null : TimeSpan.FromSeconds(reader.GetInt64(4)),
                    reader.IsDBNull(5)
                        ? null
                        : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(5)),
                    reader.GetInt64(6) != 0,
                    DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(7)),
                    reader.IsDBNull(8) ? null : reader.GetString(8));
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<UsageHistoryPoint>> LoadSeriesAsync(
        string windowKey,
        DateTimeOffset since,
        int maximumPoints = 300,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT observed_at, used_percent, reset_at
                FROM (
                    SELECT observed_at, used_percent, reset_at, id
                    FROM usage_samples
                    WHERE window_key = $window_key
                      AND observed_at >= $since
                      AND used_percent IS NOT NULL
                    ORDER BY observed_at DESC, id DESC
                    LIMIT $maximum_points
                )
                ORDER BY observed_at ASC, id ASC;
                """;
            command.Parameters.AddWithValue("$window_key", windowKey);
            command.Parameters.AddWithValue("$since", since.ToUnixTimeSeconds());
            command.Parameters.AddWithValue("$maximum_points", maximumPoints);

            var points = new List<UsageHistoryPoint>();
            long? previousResetAt = null;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var resetAt = reader.IsDBNull(2) ? (long?)null : reader.GetInt64(2);
                var isReset = previousResetAt is not null &&
                    resetAt is not null &&
                    resetAt != previousResetAt;
                previousResetAt = resetAt;
                points.Add(new UsageHistoryPoint(
                    DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0)),
                    reader.GetDouble(1),
                    isReset));
            }

            return points;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }
}

using System.Data.Common;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Data.Sqlite;

namespace GameGuard.Infrastructure.Persistence;

public sealed class SqliteSessionRepository : ISessionRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteSessionRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<GamingSession?> GetActiveSessionByProcessIdAsync(int processId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, GameProfileId, ProcessId, ProcessName, StartTimeUtc, EndTimeUtc, DurationSeconds, TerminationReason, Notes
            FROM GamingSessions
            WHERE ProcessId = $processId AND EndTimeUtc IS NULL
            ORDER BY StartTimeUtc DESC
            LIMIT 1;
        ";
        command.Parameters.AddWithValue("$processId", processId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return MapGamingSession(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<GamingSession>> GetActiveSessionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, GameProfileId, ProcessId, ProcessName, StartTimeUtc, EndTimeUtc, DurationSeconds, TerminationReason, Notes
            FROM GamingSessions
            WHERE EndTimeUtc IS NULL
            ORDER BY StartTimeUtc DESC;
        ";

        var list = new List<GamingSession>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(MapGamingSession(reader));
        }

        return list;
    }

    public async Task StartSessionAsync(GamingSession session, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO GamingSessions (Id, GameProfileId, ProcessId, ProcessName, StartTimeUtc, EndTimeUtc, DurationSeconds, TerminationReason, Notes)
            VALUES ($id, $gameProfileId, $processId, $processName, $startTimeUtc, $endTimeUtc, $durationSeconds, $terminationReason, $notes);
        ";
        command.Parameters.AddWithValue("$id", session.Id.ToString());
        command.Parameters.AddWithValue("$gameProfileId", session.GameProfileId.ToString());
        command.Parameters.AddWithValue("$processId", session.ProcessId);
        command.Parameters.AddWithValue("$processName", session.ProcessName);
        command.Parameters.AddWithValue("$startTimeUtc", session.StartTimeUtc.ToString("O"));
        command.Parameters.AddWithValue("$endTimeUtc", (object?)session.EndTimeUtc?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$durationSeconds", session.DurationSeconds);
        command.Parameters.AddWithValue("$terminationReason", (int)session.TerminationReason);
        command.Parameters.AddWithValue("$notes", (object?)session.Notes ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateSessionHeartbeatAsync(Guid sessionId, int durationSeconds, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE GamingSessions
            SET DurationSeconds = $durationSeconds
            WHERE Id = $id AND EndTimeUtc IS NULL;
        ";
        command.Parameters.AddWithValue("$id", sessionId.ToString());
        command.Parameters.AddWithValue("$durationSeconds", durationSeconds);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task EndSessionAsync(
        Guid sessionId,
        DateTime endTimeUtc,
        SessionTerminationReason reason,
        string? notes = null,
        int? durationSeconds = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE GamingSessions
            SET EndTimeUtc = $endTimeUtc,
                TerminationReason = $terminationReason,
                DurationSeconds = COALESCE($durationSeconds, DurationSeconds),
                Notes = COALESCE($notes, Notes)
            WHERE Id = $id;
        ";
        command.Parameters.AddWithValue("$id", sessionId.ToString());
        command.Parameters.AddWithValue("$endTimeUtc", endTimeUtc.ToString("O"));
        command.Parameters.AddWithValue("$terminationReason", (int)reason);
        command.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$durationSeconds", (object?)durationSeconds ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> GetTotalSecondsUsedForGameTodayAsync(
        Guid gameProfileId,
        DateTime dayStartUtc,
        DateTime dayEndUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COALESCE(SUM(DurationSeconds), 0)
            FROM GamingSessions
            WHERE GameProfileId = $gameProfileId
              AND StartTimeUtc >= $dayStartUtc
              AND StartTimeUtc < $dayEndUtc
              AND EndTimeUtc IS NOT NULL;
        ";
        command.Parameters.AddWithValue("$gameProfileId", gameProfileId.ToString());
        command.Parameters.AddWithValue("$dayStartUtc", dayStartUtc.ToString("O"));
        command.Parameters.AddWithValue("$dayEndUtc", dayEndUtc.ToString("O"));

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    public async Task<int> GetTotalSharedSecondsUsedTodayAsync(
        DateTime dayStartUtc,
        DateTime dayEndUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COALESCE(SUM(DurationSeconds), 0)
            FROM GamingSessions
            WHERE StartTimeUtc >= $dayStartUtc
              AND StartTimeUtc < $dayEndUtc
              AND EndTimeUtc IS NOT NULL;
        ";
        command.Parameters.AddWithValue("$dayStartUtc", dayStartUtc.ToString("O"));
        command.Parameters.AddWithValue("$dayEndUtc", dayEndUtc.ToString("O"));

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<GamingSession>> GetRecentSessionsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, GameProfileId, ProcessId, ProcessName, StartTimeUtc, EndTimeUtc, DurationSeconds, TerminationReason, Notes
            FROM GamingSessions
            ORDER BY StartTimeUtc DESC
            LIMIT $limit;
        ";
        command.Parameters.AddWithValue("$limit", limit);

        var list = new List<GamingSession>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(MapGamingSession(reader));
        }

        return list;
    }

    public async Task<IReadOnlyList<GamingSession>> GetSessionsForDateRangeAsync(
        DateTime startUtc,
        DateTime endUtc,
        Guid? gameProfileId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        if (gameProfileId.HasValue)
        {
            command.CommandText = @"
                SELECT Id, GameProfileId, ProcessId, ProcessName, StartTimeUtc, EndTimeUtc, DurationSeconds, TerminationReason, Notes
                FROM GamingSessions
                WHERE GameProfileId = $gameProfileId
                  AND StartTimeUtc >= $startUtc
                  AND StartTimeUtc < $endUtc
                ORDER BY StartTimeUtc DESC;
            ";
            command.Parameters.AddWithValue("$gameProfileId", gameProfileId.Value.ToString());
        }
        else
        {
            command.CommandText = @"
                SELECT Id, GameProfileId, ProcessId, ProcessName, StartTimeUtc, EndTimeUtc, DurationSeconds, TerminationReason, Notes
                FROM GamingSessions
                WHERE StartTimeUtc >= $startUtc
                  AND StartTimeUtc < $endUtc
                ORDER BY StartTimeUtc DESC;
            ";
        }

        command.Parameters.AddWithValue("$startUtc", startUtc.ToString("O"));
        command.Parameters.AddWithValue("$endUtc", endUtc.ToString("O"));

        var list = new List<GamingSession>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(MapGamingSession(reader));
        }

        return list;
    }

    private static GamingSession MapGamingSession(DbDataReader reader)
    {
        return new GamingSession
        {
            Id = Guid.Parse(reader.GetString(0)),
            GameProfileId = Guid.Parse(reader.GetString(1)),
            ProcessId = reader.GetInt32(2),
            ProcessName = reader.GetString(3),
            StartTimeUtc = DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
            EndTimeUtc = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
            DurationSeconds = reader.GetInt32(6),
            TerminationReason = (SessionTerminationReason)reader.GetInt32(7),
            Notes = reader.IsDBNull(8) ? null : reader.GetString(8)
        };
    }
}

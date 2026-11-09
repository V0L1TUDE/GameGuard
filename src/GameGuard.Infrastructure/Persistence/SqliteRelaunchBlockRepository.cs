using System.Globalization;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Data.Sqlite;

namespace GameGuard.Infrastructure.Persistence;

public sealed class SqliteRelaunchBlockRepository : IRelaunchBlockRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteRelaunchBlockRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task RecordBlockAsync(RelaunchBlockRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            INSERT INTO RelaunchBlocks (Id, GameProfileId, ProcessId, ProcessName, BlockedAtUtc, Reason)
            VALUES ($id, $gameProfileId, $processId, $processName, $blockedAtUtc, $reason);
        ";

        command.Parameters.AddWithValue("$id", record.Id.ToString());
        command.Parameters.AddWithValue("$gameProfileId", record.GameProfileId.ToString());
        command.Parameters.AddWithValue("$processId", record.ProcessId);
        command.Parameters.AddWithValue("$processName", record.ProcessName);
        command.Parameters.AddWithValue("$blockedAtUtc", record.BlockedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$reason", record.Reason);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RelaunchBlockRecord>> GetRecentBlocksAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT Id, GameProfileId, ProcessId, ProcessName, BlockedAtUtc, Reason
            FROM RelaunchBlocks
            ORDER BY BlockedAtUtc DESC
            LIMIT $limit;
        ";
        command.Parameters.AddWithValue("$limit", limit);

        var list = new List<RelaunchBlockRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new RelaunchBlockRecord
            {
                Id = Guid.Parse(reader.GetString(0)),
                GameProfileId = Guid.Parse(reader.GetString(1)),
                ProcessId = reader.GetInt32(2),
                ProcessName = reader.GetString(3),
                BlockedAtUtc = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Reason = reader.GetString(5)
            });
        }

        return list;
    }

    public async Task<int> GetBlockCountForGameTodayAsync(Guid gameProfileId, DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT COUNT(1)
            FROM RelaunchBlocks
            WHERE GameProfileId = $gameProfileId
              AND BlockedAtUtc >= $dayStartUtc
              AND BlockedAtUtc < $dayEndUtc;
        ";
        command.Parameters.AddWithValue("$gameProfileId", gameProfileId.ToString());
        command.Parameters.AddWithValue("$dayStartUtc", dayStartUtc.ToString("o"));
        command.Parameters.AddWithValue("$dayEndUtc", dayEndUtc.ToString("o"));

        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(scalar);
    }
}

using System.Data.Common;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Data.Sqlite;

namespace GameGuard.Infrastructure.Persistence;

public sealed class SqliteLimitRepository : ILimitRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteLimitRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<DailyLimit?> GetIndividualLimitAsync(Guid gameProfileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, GameProfileId, DailyLimitMinutes, ResetHourUtc, IsShared, IsActive, GracePeriodSeconds, CreatedAtUtc
            FROM DailyLimits
            WHERE GameProfileId = $gameProfileId AND IsShared = 0;
        ";
        command.Parameters.AddWithValue("$gameProfileId", gameProfileId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return MapDailyLimit(reader);
        }

        return null;
    }

    public async Task<DailyLimit?> GetSharedLimitAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, GameProfileId, DailyLimitMinutes, ResetHourUtc, IsShared, IsActive, GracePeriodSeconds, CreatedAtUtc
            FROM DailyLimits
            WHERE IsShared = 1
            LIMIT 1;
        ";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return MapDailyLimit(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<DailyLimit>> GetAllLimitsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, GameProfileId, DailyLimitMinutes, ResetHourUtc, IsShared, IsActive, GracePeriodSeconds, CreatedAtUtc
            FROM DailyLimits;
        ";

        var list = new List<DailyLimit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(MapDailyLimit(reader));
        }

        return list;
    }

    public async Task UpsertLimitAsync(DailyLimit limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        if (limit.IsShared)
        {
            // For shared limit, there should only ever be one
            command.CommandText = @"
                INSERT INTO DailyLimits (Id, GameProfileId, DailyLimitMinutes, ResetHourUtc, IsShared, IsActive, GracePeriodSeconds, CreatedAtUtc)
                VALUES ($id, NULL, $dailyLimitMinutes, $resetHourUtc, 1, $isActive, $gracePeriodSeconds, $createdAtUtc)
                ON CONFLICT(Id) DO UPDATE SET
                    DailyLimitMinutes = excluded.DailyLimitMinutes,
                    ResetHourUtc = excluded.ResetHourUtc,
                    IsActive = excluded.IsActive,
                    GracePeriodSeconds = excluded.GracePeriodSeconds;
            ";
        }
        else
        {
            command.CommandText = @"
                INSERT INTO DailyLimits (Id, GameProfileId, DailyLimitMinutes, ResetHourUtc, IsShared, IsActive, GracePeriodSeconds, CreatedAtUtc)
                VALUES ($id, $gameProfileId, $dailyLimitMinutes, $resetHourUtc, 0, $isActive, $gracePeriodSeconds, $createdAtUtc)
                ON CONFLICT(Id) DO UPDATE SET
                    DailyLimitMinutes = excluded.DailyLimitMinutes,
                    ResetHourUtc = excluded.ResetHourUtc,
                    IsActive = excluded.IsActive,
                    GracePeriodSeconds = excluded.GracePeriodSeconds;
            ";
            command.Parameters.AddWithValue("$gameProfileId", (object?)limit.GameProfileId?.ToString() ?? DBNull.Value);
        }

        command.Parameters.AddWithValue("$id", limit.Id.ToString());
        command.Parameters.AddWithValue("$dailyLimitMinutes", limit.DailyLimitMinutes);
        command.Parameters.AddWithValue("$resetHourUtc", limit.ResetHourUtc);
        command.Parameters.AddWithValue("$isActive", limit.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$gracePeriodSeconds", limit.GracePeriodSeconds);
        command.Parameters.AddWithValue("$createdAtUtc", limit.CreatedAtUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteLimitAsync(Guid limitId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DailyLimits WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", limitId.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DailyLimit MapDailyLimit(DbDataReader reader)
    {
        return new DailyLimit
        {
            Id = Guid.Parse(reader.GetString(0)),
            GameProfileId = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            DailyLimitMinutes = reader.GetInt32(2),
            ResetHourUtc = reader.GetInt32(3),
            IsShared = reader.GetInt32(4) == 1,
            IsActive = reader.GetInt32(5) == 1,
            GracePeriodSeconds = reader.GetInt32(6),
            CreatedAtUtc = DateTime.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }
}

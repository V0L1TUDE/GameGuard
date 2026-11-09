using System.Globalization;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Data.Sqlite;

namespace GameGuard.Infrastructure.Persistence;

public sealed class SqliteAdminOverrideRepository : IAdminOverrideRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteAdminOverrideRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AdminOverride>> GetActiveOverridesAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT Id, GameProfileId, Type, BonusMinutes, CreatedAtUtc, ExpiresAtUtc, Reason, IsRevoked, RevokedAtUtc
            FROM AdminOverrides
            WHERE IsRevoked = 0 AND ExpiresAtUtc > $nowUtc
            ORDER BY CreatedAtUtc DESC;
        ";
        command.Parameters.AddWithValue("$nowUtc", nowUtc.ToString("o"));

        var list = new List<AdminOverride>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadOverride(reader));
        }

        return list;
    }

    public async Task<AdminOverride?> GetActiveOverrideForGameAsync(Guid gameProfileId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // Specific game override takes precedence, followed by global override (GameProfileId IS NULL)
        command.CommandText = @"
            SELECT Id, GameProfileId, Type, BonusMinutes, CreatedAtUtc, ExpiresAtUtc, Reason, IsRevoked, RevokedAtUtc
            FROM AdminOverrides
            WHERE IsRevoked = 0 
              AND ExpiresAtUtc > $nowUtc
              AND (GameProfileId = $gameProfileId OR GameProfileId IS NULL)
            ORDER BY CASE WHEN GameProfileId IS NOT NULL THEN 0 ELSE 1 END, CreatedAtUtc DESC
            LIMIT 1;
        ";
        command.Parameters.AddWithValue("$nowUtc", nowUtc.ToString("o"));
        command.Parameters.AddWithValue("$gameProfileId", gameProfileId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return ReadOverride(reader);
        }

        return null;
    }

    public async Task AddOverrideAsync(AdminOverride adminOverride, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            INSERT INTO AdminOverrides (Id, GameProfileId, Type, BonusMinutes, CreatedAtUtc, ExpiresAtUtc, Reason, IsRevoked, RevokedAtUtc)
            VALUES ($id, $gameProfileId, $type, $bonusMinutes, $createdAtUtc, $expiresAtUtc, $reason, $isRevoked, $revokedAtUtc);
        ";

        command.Parameters.AddWithValue("$id", adminOverride.Id.ToString());
        command.Parameters.AddWithValue("$gameProfileId", adminOverride.GameProfileId.HasValue ? adminOverride.GameProfileId.Value.ToString() : (object)DBNull.Value);
        command.Parameters.AddWithValue("$type", (int)adminOverride.Type);
        command.Parameters.AddWithValue("$bonusMinutes", adminOverride.BonusMinutes);
        command.Parameters.AddWithValue("$createdAtUtc", adminOverride.CreatedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$expiresAtUtc", adminOverride.ExpiresAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$reason", adminOverride.Reason ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$isRevoked", adminOverride.IsRevoked ? 1 : 0);
        command.Parameters.AddWithValue("$revokedAtUtc", adminOverride.RevokedAtUtc.HasValue ? adminOverride.RevokedAtUtc.Value.ToString("o") : (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RevokeOverrideAsync(Guid overrideId, DateTime revokedAtUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            UPDATE AdminOverrides
            SET IsRevoked = 1, RevokedAtUtc = $revokedAtUtc
            WHERE Id = $id;
        ";

        command.Parameters.AddWithValue("$id", overrideId.ToString());
        command.Parameters.AddWithValue("$revokedAtUtc", revokedAtUtc.ToString("o"));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AdminOverride>> GetOverrideHistoryAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT Id, GameProfileId, Type, BonusMinutes, CreatedAtUtc, ExpiresAtUtc, Reason, IsRevoked, RevokedAtUtc
            FROM AdminOverrides
            ORDER BY CreatedAtUtc DESC
            LIMIT $limit;
        ";
        command.Parameters.AddWithValue("$limit", limit);

        var list = new List<AdminOverride>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadOverride(reader));
        }

        return list;
    }

    private static AdminOverride ReadOverride(SqliteDataReader reader)
    {
        return new AdminOverride
        {
            Id = Guid.Parse(reader.GetString(0)),
            GameProfileId = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            Type = (OverrideType)reader.GetInt32(2),
            BonusMinutes = reader.GetInt32(3),
            CreatedAtUtc = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            ExpiresAtUtc = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            Reason = reader.IsDBNull(6) ? null : reader.GetString(6),
            IsRevoked = reader.GetInt32(7) == 1,
            RevokedAtUtc = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        };
    }
}

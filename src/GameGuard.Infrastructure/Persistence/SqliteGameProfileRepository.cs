using System.Data.Common;
using System.Text.Json;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Data.Sqlite;

namespace GameGuard.Infrastructure.Persistence;

public sealed class SqliteGameProfileRepository : IGameProfileRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteGameProfileRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<GameProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, Name, ProcessNames, ExecutablePath, IsMonitored, CreatedAtUtc, UpdatedAtUtc
            FROM GameProfiles
            WHERE Id = $id;
        ";
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return MapGameProfile(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<GameProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, Name, ProcessNames, ExecutablePath, IsMonitored, CreatedAtUtc, UpdatedAtUtc
            FROM GameProfiles
            ORDER BY Name ASC;
        ";

        var list = new List<GameProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(MapGameProfile(reader));
        }

        return list;
    }

    public async Task<IReadOnlyList<GameProfile>> GetMonitoredAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, Name, ProcessNames, ExecutablePath, IsMonitored, CreatedAtUtc, UpdatedAtUtc
            FROM GameProfiles
            WHERE IsMonitored = 1
            ORDER BY Name ASC;
        ";

        var list = new List<GameProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(MapGameProfile(reader));
        }

        return list;
    }

    public async Task<GameProfile?> FindByProcessNameAsync(string processName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        var monitoredGames = await GetMonitoredAsync(cancellationToken).ConfigureAwait(false);
        return monitoredGames.FirstOrDefault(g => g.MatchesProcess(processName));
    }

    public async Task AddAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO GameProfiles (Id, Name, ProcessNames, ExecutablePath, IsMonitored, CreatedAtUtc, UpdatedAtUtc)
            VALUES ($id, $name, $processNames, $executablePath, $isMonitored, $createdAtUtc, $updatedAtUtc);
        ";
        command.Parameters.AddWithValue("$id", profile.Id.ToString());
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$processNames", JsonSerializer.Serialize(profile.ProcessNames));
        command.Parameters.AddWithValue("$executablePath", (object?)profile.ExecutablePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$isMonitored", profile.IsMonitored ? 1 : 0);
        command.Parameters.AddWithValue("$createdAtUtc", profile.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", profile.UpdatedAtUtc.HasValue ? profile.UpdatedAtUtc.Value.ToString("O") : DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE GameProfiles
            SET Name = $name,
                ProcessNames = $processNames,
                ExecutablePath = $executablePath,
                IsMonitored = $isMonitored,
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $id;
        ";
        command.Parameters.AddWithValue("$id", profile.Id.ToString());
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$processNames", JsonSerializer.Serialize(profile.ProcessNames));
        command.Parameters.AddWithValue("$executablePath", (object?)profile.ExecutablePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$isMonitored", profile.IsMonitored ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAtUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM GameProfiles WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static GameProfile MapGameProfile(DbDataReader reader)
    {
        var processNamesJson = reader.GetString(2);
        var processNames = JsonSerializer.Deserialize<List<string>>(processNamesJson) ?? new List<string>();

        return new GameProfile
        {
            Id = Guid.Parse(reader.GetString(0)),
            Name = reader.GetString(1),
            ProcessNames = processNames,
            ExecutablePath = reader.IsDBNull(3) ? null : reader.GetString(3),
            IsMonitored = reader.GetInt32(4) == 1,
            CreatedAtUtc = DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
            UpdatedAtUtc = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }
}

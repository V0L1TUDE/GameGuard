using System.Text.Json;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace GameGuard.Infrastructure.Persistence;

public sealed class SqliteDatabaseInitializer : IDatabaseInitializer
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SqliteDatabaseInitializer> _logger;

    public SqliteDatabaseInitializer(SqliteConnectionFactory connectionFactory, ILogger<SqliteDatabaseInitializer> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS GameProfiles (
                        Id TEXT PRIMARY KEY NOT NULL,
                        Name TEXT NOT NULL,
                        ProcessNames TEXT NOT NULL,
                        ExecutablePath TEXT,
                        IsMonitored INTEGER NOT NULL DEFAULT 1,
                        CreatedAtUtc TEXT NOT NULL,
                        UpdatedAtUtc TEXT
                    );

                    CREATE TABLE IF NOT EXISTS DailyLimits (
                        Id TEXT PRIMARY KEY NOT NULL,
                        GameProfileId TEXT,
                        DailyLimitMinutes INTEGER NOT NULL,
                        ResetHourUtc INTEGER NOT NULL DEFAULT 0,
                        IsShared INTEGER NOT NULL DEFAULT 0,
                        IsActive INTEGER NOT NULL DEFAULT 1,
                        GracePeriodSeconds INTEGER NOT NULL DEFAULT 60,
                        CreatedAtUtc TEXT NOT NULL,
                        FOREIGN KEY (GameProfileId) REFERENCES GameProfiles(Id) ON DELETE CASCADE
                    );

                    CREATE TABLE IF NOT EXISTS GamingSessions (
                        Id TEXT PRIMARY KEY NOT NULL,
                        GameProfileId TEXT NOT NULL,
                        ProcessId INTEGER NOT NULL,
                        ProcessName TEXT NOT NULL,
                        StartTimeUtc TEXT NOT NULL,
                        EndTimeUtc TEXT,
                        DurationSeconds INTEGER NOT NULL DEFAULT 0,
                        TerminationReason INTEGER NOT NULL DEFAULT 0,
                        Notes TEXT,
                        FOREIGN KEY (GameProfileId) REFERENCES GameProfiles(Id) ON DELETE CASCADE
                    );

                    CREATE INDEX IF NOT EXISTS IX_GamingSessions_GameProfile_StartTime 
                        ON GamingSessions (GameProfileId, StartTimeUtc);

                    CREATE INDEX IF NOT EXISTS IX_GamingSessions_Active 
                        ON GamingSessions (EndTimeUtc);

                    CREATE INDEX IF NOT EXISTS IX_DailyLimits_Shared 
                        ON DailyLimits (IsShared);
                ";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // Seed default games if table is empty
            await using (var countCommand = connection.CreateCommand())
            {
                countCommand.Transaction = transaction;
                countCommand.CommandText = "SELECT COUNT(1) FROM GameProfiles;";
                var count = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

                if (count == 0)
                {
                    _logger.LogInformation("Seeding default game profiles for Counter-Strike 2 and Valorant.");

                    var defaults = new List<GameProfile>
                    {
                        new()
                        {
                            Id = Guid.NewGuid(),
                            Name = "Counter-Strike 2",
                            ProcessNames = new[] { "cs2.exe" },
                            IsMonitored = true,
                            CreatedAtUtc = DateTime.UtcNow
                        },
                        new()
                        {
                            Id = Guid.NewGuid(),
                            Name = "Valorant",
                            ProcessNames = new[] { "VALORANT-Win64-Shipping.exe", "VALORANT.exe" },
                            IsMonitored = true,
                            CreatedAtUtc = DateTime.UtcNow
                        }
                    };

                    foreach (var game in defaults)
                    {
                        await using var insertCmd = connection.CreateCommand();
                        insertCmd.Transaction = transaction;
                        insertCmd.CommandText = @"
                            INSERT INTO GameProfiles (Id, Name, ProcessNames, ExecutablePath, IsMonitored, CreatedAtUtc)
                            VALUES ($id, $name, $processNames, $executablePath, $isMonitored, $createdAtUtc);
                        ";
                        insertCmd.Parameters.AddWithValue("$id", game.Id.ToString());
                        insertCmd.Parameters.AddWithValue("$name", game.Name);
                        insertCmd.Parameters.AddWithValue("$processNames", JsonSerializer.Serialize(game.ProcessNames));
                        insertCmd.Parameters.AddWithValue("$executablePath", (object?)game.ExecutablePath ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("$isMonitored", game.IsMonitored ? 1 : 0);
                        insertCmd.Parameters.AddWithValue("$createdAtUtc", game.CreatedAtUtc.ToString("O"));

                        await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("GameGuard database schema initialized successfully.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "Failed to initialize GameGuard SQLite database schema.");
            throw;
        }
    }
}

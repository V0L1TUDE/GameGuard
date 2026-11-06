using GameGuard.Core.Models;
using GameGuard.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class SqliteRepositoryTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteDatabaseInitializer _initializer;
    private readonly SqliteGameProfileRepository _gameRepo;
    private readonly SqliteLimitRepository _limitRepo;
    private readonly SqliteSessionRepository _sessionRepo;

    public SqliteRepositoryTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"gameguard_test_{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new SqliteDatabaseInitializer(_connectionFactory, NullLogger<SqliteDatabaseInitializer>.Instance);
        _gameRepo = new SqliteGameProfileRepository(_connectionFactory);
        _limitRepo = new SqliteLimitRepository(_connectionFactory);
        _sessionRepo = new SqliteSessionRepository(_connectionFactory);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch
        {
            // Ignore temporary test cleanup file locks
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesTablesAndSeedsDefaultGames()
    {
        await _initializer.InitializeAsync();

        var games = await _gameRepo.GetAllAsync();
        Assert.NotEmpty(games);
        Assert.Contains(games, g => g.Name == "Counter-Strike 2");
        Assert.Contains(games, g => g.Name == "Valorant");

        var cs2 = await _gameRepo.FindByProcessNameAsync("cs2.exe");
        Assert.NotNull(cs2);
        Assert.Equal("Counter-Strike 2", cs2.Name);
    }

    [Fact]
    public async Task GameProfileRepository_CrudOperations_WorkCorrectly()
    {
        await _initializer.InitializeAsync();

        var newGame = new GameProfile
        {
            Id = Guid.NewGuid(),
            Name = "Apex Legends",
            ProcessNames = new[] { "r5apex.exe" },
            IsMonitored = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _gameRepo.AddAsync(newGame);

        var retrieved = await _gameRepo.GetByIdAsync(newGame.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Apex Legends", retrieved.Name);
        Assert.True(retrieved.MatchesProcess("r5apex.exe"));

        retrieved.Name = "Apex Legends Updated";
        await _gameRepo.UpdateAsync(retrieved);

        var updated = await _gameRepo.GetByIdAsync(newGame.Id);
        Assert.NotNull(updated);
        Assert.Equal("Apex Legends Updated", updated.Name);

        await _gameRepo.DeleteAsync(newGame.Id);
        var deleted = await _gameRepo.GetByIdAsync(newGame.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task LimitRepository_UpsertAndRetrieve_WorksCorrectly()
    {
        await _initializer.InitializeAsync();

        var games = await _gameRepo.GetAllAsync();
        var cs2 = games.First(g => g.Name == "Counter-Strike 2");

        var individualLimit = DailyLimit.CreateIndividual(cs2.Id, minutes: 90, gracePeriodSeconds: 120);
        await _limitRepo.UpsertLimitAsync(individualLimit);

        var retrievedInd = await _limitRepo.GetIndividualLimitAsync(cs2.Id);
        Assert.NotNull(retrievedInd);
        Assert.Equal(90, retrievedInd.DailyLimitMinutes);
        Assert.Equal(120, retrievedInd.GracePeriodSeconds);

        var sharedLimit = DailyLimit.CreateShared(minutes: 150, gracePeriodSeconds: 60);
        await _limitRepo.UpsertLimitAsync(sharedLimit);

        var retrievedShared = await _limitRepo.GetSharedLimitAsync();
        Assert.NotNull(retrievedShared);
        Assert.True(retrievedShared.IsShared);
        Assert.Equal(150, retrievedShared.DailyLimitMinutes);
    }

    [Fact]
    public async Task SessionRepository_FullSessionLifecycleAndAggregateUsage_WorkCorrectly()
    {
        await _initializer.InitializeAsync();

        var games = await _gameRepo.GetAllAsync();
        var cs2 = games.First(g => g.Name == "Counter-Strike 2");

        var now = DateTime.UtcNow;
        var dayStart = now.Date;
        var dayEnd = dayStart.AddDays(1);

        var session = new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = cs2.Id,
            ProcessId = 12345,
            ProcessName = "cs2.exe",
            StartTimeUtc = now.AddMinutes(-30),
            DurationSeconds = 0,
            TerminationReason = SessionTerminationReason.Active
        };

        await _sessionRepo.StartSessionAsync(session);

        var active = await _sessionRepo.GetActiveSessionByProcessIdAsync(12345);
        Assert.NotNull(active);
        Assert.Equal(session.Id, active.Id);
        Assert.True(active.IsActive);

        // Heartbeat update
        await _sessionRepo.UpdateSessionHeartbeatAsync(session.Id, durationSeconds: 900);

        // End session
        await _sessionRepo.EndSessionAsync(session.Id, now, SessionTerminationReason.LimitEnforced, "Exceeded limit");

        var closed = await _sessionRepo.GetActiveSessionByProcessIdAsync(12345);
        Assert.Null(closed);

        // Verify total daily duration aggregation
        var totalCs2Usage = await _sessionRepo.GetTotalSecondsUsedForGameTodayAsync(cs2.Id, dayStart, dayEnd);
        Assert.Equal(900, totalCs2Usage);

        var totalSharedUsage = await _sessionRepo.GetTotalSharedSecondsUsedTodayAsync(dayStart, dayEnd);
        Assert.Equal(900, totalSharedUsage);
    }
}

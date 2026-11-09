using GameGuard.Core.Models;
using GameGuard.Core.Services;
using GameGuard.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class AdminOverrideRepositoryAndLimitTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteDatabaseInitializer _initializer;
    private readonly SqliteAdminOverrideRepository _overrideRepo;
    private readonly LimitCalculator _calculator;

    public AdminOverrideRepositoryAndLimitTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"gameguard_override_{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new SqliteDatabaseInitializer(_connectionFactory, NullLogger<SqliteDatabaseInitializer>.Instance);
        _overrideRepo = new SqliteAdminOverrideRepository(_connectionFactory);
        _calculator = new LimitCalculator();
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
        }
    }

    [Fact]
    public async Task AddOverrideAsync_AndGetActiveOverrideForGameAsync_RetrievesActiveOverride()
    {
        await _initializer.InitializeAsync();
        var gameRepo = new SqliteGameProfileRepository(_connectionFactory);
        var games = await gameRepo.GetAllAsync();
        var gameId = games.First().Id;
        var now = new DateTime(2026, 11, 9, 14, 0, 0, DateTimeKind.Utc);

        var bonusOverride = AdminOverride.CreateBonusTime(gameId, 30, now, TimeSpan.FromHours(2), "Good behavior reward");
        await _overrideRepo.AddOverrideAsync(bonusOverride);

        var active = await _overrideRepo.GetActiveOverrideForGameAsync(gameId, now.AddMinutes(30));
        Assert.NotNull(active);
        Assert.Equal(gameId, active!.GameProfileId);
        Assert.Equal(OverrideType.BonusTime, active.Type);
        Assert.Equal(30, active.BonusMinutes);

        // When time advances past expiration, it is no longer returned
        var expired = await _overrideRepo.GetActiveOverrideForGameAsync(gameId, now.AddHours(3));
        Assert.Null(expired);
    }

    [Fact]
    public async Task RevokeOverrideAsync_DeactivatesOverrideImmediately()
    {
        await _initializer.InitializeAsync();
        var gameRepo = new SqliteGameProfileRepository(_connectionFactory);
        var games = await gameRepo.GetAllAsync();
        var gameId = games.First().Id;
        var now = new DateTime(2026, 11, 9, 14, 0, 0, DateTimeKind.Utc);

        var unlock = AdminOverride.CreateTemporaryUnlock(gameId, now, TimeSpan.FromHours(1), "Testing");
        await _overrideRepo.AddOverrideAsync(unlock);

        var activeBefore = await _overrideRepo.GetActiveOverrideForGameAsync(gameId, now.AddMinutes(10));
        Assert.NotNull(activeBefore);

        await _overrideRepo.RevokeOverrideAsync(unlock.Id, now.AddMinutes(15));

        var activeAfter = await _overrideRepo.GetActiveOverrideForGameAsync(gameId, now.AddMinutes(20));
        Assert.Null(activeAfter);
    }

    [Fact]
    public void LimitCalculator_WithEmergencyLockOverride_ForcesLimitExceededImmediately()
    {
        var gameId = Guid.NewGuid();
        var limit = DailyLimit.CreateIndividual(gameId, 120); // 120 min allowed
        var emergencyLock = AdminOverride.CreateEmergencyLock(gameId, DateTime.UtcNow, TimeSpan.FromHours(1));

        // Player only used 10 minutes so far
        var result = _calculator.Calculate(
            gameId,
            "CS2",
            limit,
            null,
            individualSecondsUsedToday: 600,
            totalSharedSecondsUsedToday: 0,
            activeSessionDurationSeconds: 0,
            activeOverride: emergencyLock);

        Assert.True(result.IsLimitExceeded);
        Assert.Equal(0, result.EffectiveLimitMinutes);
        Assert.Equal(OverrideType.EmergencyLock, result.ActiveOverrideType);
        Assert.True(result.HasActiveOverride);
    }

    [Fact]
    public void LimitCalculator_WithTemporaryUnlockOverride_AllowsPlayEvenIfQuotaExhausted()
    {
        var gameId = Guid.NewGuid();
        var limit = DailyLimit.CreateIndividual(gameId, 60); // 60 min allowed
        var unlock = AdminOverride.CreateTemporaryUnlock(gameId, DateTime.UtcNow, TimeSpan.FromHours(1));

        // Player used 90 minutes (overdue by 30 min)
        var result = _calculator.Calculate(
            gameId,
            "CS2",
            limit,
            null,
            individualSecondsUsedToday: 5400,
            totalSharedSecondsUsedToday: 0,
            activeSessionDurationSeconds: 0,
            activeOverride: unlock);

        Assert.False(result.IsLimitExceeded);
        Assert.Equal(OverrideType.TemporaryUnlock, result.ActiveOverrideType);
        Assert.True(result.HasActiveOverride);
    }

    [Fact]
    public void LimitCalculator_WithBonusTimeOverride_AddsAllowedMinutesToQuota()
    {
        var gameId = Guid.NewGuid();
        var limit = DailyLimit.CreateIndividual(gameId, 60); // 60 min
        var bonus = AdminOverride.CreateBonusTime(gameId, 30, DateTime.UtcNow, TimeSpan.FromHours(2)); // +30 min -> 90 min total

        // Player used 70 minutes (would normally be exceeded)
        var result = _calculator.Calculate(
            gameId,
            "CS2",
            limit,
            null,
            individualSecondsUsedToday: 4200,
            totalSharedSecondsUsedToday: 0,
            activeSessionDurationSeconds: 0,
            activeOverride: bonus);

        // 90m allowed - 70m used = 20m remaining (1200s)
        Assert.False(result.IsLimitExceeded);
        Assert.Equal(90, result.EffectiveLimitMinutes);
        Assert.Equal(1200, result.SecondsRemaining);
        Assert.Equal(30, result.BonusMinutesApplied);
    }
}

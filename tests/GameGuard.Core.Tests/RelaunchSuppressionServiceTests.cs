using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using GameGuard.Core.Services;
using GameGuard.Core.Tests.Mocks;
using GameGuard.Infrastructure.Enforcement;
using GameGuard.Infrastructure.Limits;
using GameGuard.Infrastructure.Monitoring;
using GameGuard.Infrastructure.Persistence;
using GameGuard.Infrastructure.Tracking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class RelaunchSuppressionServiceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteDatabaseInitializer _initializer;
    private readonly SqliteGameProfileRepository _gameRepo;
    private readonly SqliteLimitRepository _limitRepo;
    private readonly SqliteSessionRepository _sessionRepo;
    private readonly SqliteAdminOverrideRepository _overrideRepo;
    private readonly SqliteRelaunchBlockRepository _blockRepo;
    private readonly MockProcessScanner _scanner;
    private readonly MockMonotonicClock _monotonicClock;
    private readonly MockSystemClock _systemClock;
    private readonly MockProcessTerminator _terminator;
    private readonly ProcessMonitor _processMonitor;
    private readonly TimeTrackerService _timeTracker;
    private readonly LimitCalculator _limitCalculator;
    private readonly DayBoundaryCalculator _boundaryCalculator;
    private readonly LimitManagerService _limitManager;

    private GameProfile _cs2 = null!;

    public RelaunchSuppressionServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"gameguard_relaunch_{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new SqliteDatabaseInitializer(_connectionFactory, NullLogger<SqliteDatabaseInitializer>.Instance);
        _gameRepo = new SqliteGameProfileRepository(_connectionFactory);
        _limitRepo = new SqliteLimitRepository(_connectionFactory);
        _sessionRepo = new SqliteSessionRepository(_connectionFactory);
        _overrideRepo = new SqliteAdminOverrideRepository(_connectionFactory);
        _blockRepo = new SqliteRelaunchBlockRepository(_connectionFactory);
        _scanner = new MockProcessScanner();
        _monotonicClock = new MockMonotonicClock();
        _systemClock = new MockSystemClock();
        _terminator = new MockProcessTerminator();

        _processMonitor = new ProcessMonitor(
            _scanner,
            _gameRepo,
            _systemClock,
            NullLogger<ProcessMonitor>.Instance);

        _timeTracker = new TimeTrackerService(
            _processMonitor,
            _sessionRepo,
            _gameRepo,
            _scanner,
            _systemClock,
            _monotonicClock,
            NullLogger<TimeTrackerService>.Instance);

        _limitCalculator = new LimitCalculator();
        _boundaryCalculator = new DayBoundaryCalculator();

        _limitManager = new LimitManagerService(
            _gameRepo,
            _limitRepo,
            _sessionRepo,
            _timeTracker,
            _limitCalculator,
            _boundaryCalculator,
            _systemClock,
            NullLogger<LimitManagerService>.Instance,
            timeZone: TimeZoneInfo.Utc,
            warningThresholdsMinutes: new[] { 15, 5, 1 },
            evaluationInterval: TimeSpan.FromSeconds(1),
            overrideRepository: _overrideRepo);
    }

    public void Dispose()
    {
        _limitManager.Dispose();
        _timeTracker.Dispose();
        _processMonitor.Dispose();
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

    private async Task<RelaunchSuppressionService> CreateServiceAsync()
    {
        await _initializer.InitializeAsync();
        var allGames = await _gameRepo.GetAllAsync();
        _cs2 = allGames.First(g => g.Name == "Counter-Strike 2");

        return new RelaunchSuppressionService(
            _processMonitor,
            _limitManager,
            _terminator,
            _blockRepo,
            _sessionRepo,
            _timeTracker,
            _systemClock,
            NullLogger<RelaunchSuppressionService>.Instance);
    }

    [Fact]
    public async Task CheckAndSuppressIfExhaustedAsync_WhenWithinQuota_DoesNotSuppress()
    {
        using var service = await CreateServiceAsync();

        // 60 minute limit, zero usage
        await _limitRepo.UpsertLimitAsync(DailyLimit.CreateIndividual(_cs2.Id, 60));

        bool suppressed = await service.CheckAndSuppressIfExhaustedAsync(_cs2, 7001, "cs2.exe");

        Assert.False(suppressed);
        Assert.Empty(_terminator.Calls);
    }

    [Fact]
    public async Task CheckAndSuppressIfExhaustedAsync_WhenQuotaExhausted_SuppressesAndRecordsBlock()
    {
        using var service = await CreateServiceAsync();

        // 1 minute limit (0 grace period)
        await _limitRepo.UpsertLimitAsync(DailyLimit.CreateIndividual(_cs2.Id, 1, gracePeriodSeconds: 0));

        // Record a finished session of 120s (exceeded limit)
        var now = new DateTime(2026, 11, 9, 14, 0, 0, DateTimeKind.Utc);
        _systemClock.UtcNow = now;
        await _sessionRepo.StartSessionAsync(new GamingSession
        {
            GameProfileId = _cs2.Id,
            ProcessId = 7000,
            ProcessName = "cs2.exe",
            StartTimeUtc = now.AddMinutes(-10),
            EndTimeUtc = now.AddMinutes(-8),
            DurationSeconds = 120,
            TerminationReason = SessionTerminationReason.LimitEnforced
        });

        RelaunchBlockedEventArgs? receivedEvent = null;
        service.RelaunchBlocked += (s, e) => receivedEvent = e;

        // Player launches CS2 again
        bool suppressed = await service.CheckAndSuppressIfExhaustedAsync(_cs2, 7002, "cs2.exe");

        Assert.True(suppressed);
        Assert.Single(_terminator.Calls);
        Assert.Equal(7002, _terminator.Calls[0].ProcessId);

        // Verify RelaunchBlocked event
        Assert.NotNull(receivedEvent);
        Assert.Equal(7002, receivedEvent!.ProcessId);
        Assert.Contains("Daily gaming limit is exhausted", receivedEvent.Reason);

        // Verify block recorded in repository
        var blocks = await _blockRepo.GetRecentBlocksAsync();
        var block = Assert.Single(blocks);
        Assert.Equal(_cs2.Id, block.GameProfileId);
        Assert.Equal(7002, block.ProcessId);
    }

    [Fact]
    public async Task CheckAndSuppressIfExhaustedAsync_WithRepeatedAttempts_EnforcesCooldown()
    {
        using var service = await CreateServiceAsync();
        await _limitRepo.UpsertLimitAsync(DailyLimit.CreateIndividual(_cs2.Id, 1, gracePeriodSeconds: 0));

        var now = new DateTime(2026, 11, 9, 14, 0, 0, DateTimeKind.Utc);
        _systemClock.UtcNow = now;
        await _sessionRepo.StartSessionAsync(new GamingSession
        {
            GameProfileId = _cs2.Id,
            ProcessId = 7000,
            ProcessName = "cs2.exe",
            StartTimeUtc = now.AddMinutes(-5),
            EndTimeUtc = now.AddMinutes(-3),
            DurationSeconds = 120,
            TerminationReason = SessionTerminationReason.LimitEnforced
        });

        // Attempt 1
        await service.CheckAndSuppressIfExhaustedAsync(_cs2, 7011, "cs2.exe");
        Assert.False(service.IsInCooldown(_cs2.Id, out _));

        // Attempt 2
        _systemClock.Advance(TimeSpan.FromSeconds(5));
        await service.CheckAndSuppressIfExhaustedAsync(_cs2, 7012, "cs2.exe");
        Assert.False(service.IsInCooldown(_cs2.Id, out _));

        // Attempt 3 (triggers cooldown!)
        _systemClock.Advance(TimeSpan.FromSeconds(5));
        await service.CheckAndSuppressIfExhaustedAsync(_cs2, 7013, "cs2.exe");
        Assert.True(service.IsInCooldown(_cs2.Id, out var cooldownRemaining));
        Assert.True(cooldownRemaining.TotalMinutes > 4);

        // Attempt 4 while in cooldown
        _systemClock.Advance(TimeSpan.FromSeconds(5));
        RelaunchBlockedEventArgs? cooldownBlockEvent = null;
        service.RelaunchBlocked += (s, e) => cooldownBlockEvent = e;

        bool suppressedInCooldown = await service.CheckAndSuppressIfExhaustedAsync(_cs2, 7014, "cs2.exe");
        Assert.True(suppressedInCooldown);
        Assert.NotNull(cooldownBlockEvent);
        Assert.Contains("Cooldown active", cooldownBlockEvent!.Reason);
    }

    [Fact]
    public async Task CheckAndSuppressIfExhaustedAsync_WhenActiveTemporaryUnlockOverrideExists_AllowsLaunch()
    {
        using var service = await CreateServiceAsync();

        // 1 minute limit (0 grace period)
        await _limitRepo.UpsertLimitAsync(DailyLimit.CreateIndividual(_cs2.Id, 1, gracePeriodSeconds: 0));

        var now = new DateTime(2026, 11, 9, 14, 0, 0, DateTimeKind.Utc);
        _systemClock.UtcNow = now;

        // Player already used 120s
        await _sessionRepo.StartSessionAsync(new GamingSession
        {
            GameProfileId = _cs2.Id,
            ProcessId = 7000,
            ProcessName = "cs2.exe",
            StartTimeUtc = now.AddMinutes(-10),
            EndTimeUtc = now.AddMinutes(-8),
            DurationSeconds = 120,
            TerminationReason = SessionTerminationReason.LimitEnforced
        });

        // Admin grants 30-minute temporary unlock
        await _overrideRepo.AddOverrideAsync(AdminOverride.CreateTemporaryUnlock(
            _cs2.Id,
            now,
            TimeSpan.FromMinutes(30),
            "Parent unlocked game"));

        bool suppressed = await service.CheckAndSuppressIfExhaustedAsync(_cs2, 7020, "cs2.exe");

        // Temporary unlock permits game launch!
        Assert.False(suppressed);
        Assert.Empty(_terminator.Calls);
    }

    [Fact]
    public async Task RelaunchSuppressionService_StartAndStop_ManagesLifecycleCorrectly()
    {
        using var service = await CreateServiceAsync();

        Assert.False(service.IsRunning);

        await service.StartAsync();
        Assert.True(service.IsRunning);

        await service.StopAsync();
        Assert.False(service.IsRunning);
    }
}

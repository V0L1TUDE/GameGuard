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

public sealed class EnforcementServiceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteDatabaseInitializer _initializer;
    private readonly SqliteGameProfileRepository _gameRepo;
    private readonly SqliteLimitRepository _limitRepo;
    private readonly SqliteSessionRepository _sessionRepo;
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
    private GameProfile _valorant = null!;

    public EnforcementServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"gameguard_enforcement_{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new SqliteDatabaseInitializer(_connectionFactory, NullLogger<SqliteDatabaseInitializer>.Instance);
        _gameRepo = new SqliteGameProfileRepository(_connectionFactory);
        _limitRepo = new SqliteLimitRepository(_connectionFactory);
        _sessionRepo = new SqliteSessionRepository(_connectionFactory);
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
            NullLogger<TimeTrackerService>.Instance,
            checkpointInterval: TimeSpan.FromSeconds(5),
            suspensionGapThreshold: TimeSpan.FromHours(24));

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
            evaluationInterval: TimeSpan.FromSeconds(1));
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

    private async Task<EnforcementService> CreateServiceAsync()
    {
        await _initializer.InitializeAsync();
        var allGames = await _gameRepo.GetAllAsync();
        _cs2 = allGames.First(g => g.Name == "Counter-Strike 2");
        _valorant = allGames.First(g => g.Name == "Valorant");

        return new EnforcementService(
            _limitManager,
            _timeTracker,
            _gameRepo,
            _sessionRepo,
            _terminator,
            _scanner,
            _systemClock,
            NullLogger<EnforcementService>.Instance);
    }

    [Fact]
    public async Task EnforceGameAsync_WithGracefulExit_ReturnsGracefulSuccessAndRecordsLimitEnforcedInDb()
    {
        using var enforcement = await CreateServiceAsync();

        _systemClock.UtcNow = new DateTime(2026, 11, 9, 14, 0, 0, DateTimeKind.Utc);
        _scanner.AddProcess(5000, "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        var activeSessions = _timeTracker.GetActiveSessions();
        Assert.Single(activeSessions);

        _terminator.SuccessToReturn = true;
        _terminator.MethodToReturn = EnforcementMethod.GracefulClose;

        GameEnforcedEventArgs? receivedEvent = null;
        enforcement.GameEnforced += (sender, e) => receivedEvent = e;

        var result = await enforcement.EnforceGameAsync(_cs2, 5000, "cs2.exe");

        Assert.True(result.Success);
        Assert.Equal(EnforcementMethod.GracefulClose, result.MethodUsed);
        Assert.Equal(5000, result.ProcessId);
        Assert.Equal("Counter-Strike 2", result.GameName);
        Assert.NotNull(receivedEvent);
        Assert.Equal(result, receivedEvent!.Result);

        // Verify session in SQLite database is updated with LimitEnforced
        var history = await _sessionRepo.GetRecentSessionsAsync(10);
        var session = Assert.Single(history, s => s.GameProfileId == _cs2.Id);
        Assert.Equal(SessionTerminationReason.LimitEnforced, session.TerminationReason);
    }

    [Fact]
    public async Task EnforceGameAsync_WhenGracefulFails_ForcesTerminationAndRecordsLimitEnforced()
    {
        using var enforcement = await CreateServiceAsync();

        _systemClock.UtcNow = new DateTime(2026, 11, 9, 14, 0, 0, DateTimeKind.Utc);
        _scanner.AddProcess(5001, "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        _terminator.SuccessToReturn = true;
        _terminator.MethodToReturn = EnforcementMethod.ForcedTermination;

        var result = await enforcement.EnforceGameAsync(_cs2, 5001, "cs2.exe");

        Assert.True(result.Success);
        Assert.Equal(EnforcementMethod.ForcedTermination, result.MethodUsed);

        var history = await _sessionRepo.GetRecentSessionsAsync(10);
        var session = Assert.Single(history, s => s.GameProfileId == _cs2.Id);
        Assert.Equal(SessionTerminationReason.LimitEnforced, session.TerminationReason);
    }

    [Fact]
    public async Task EnforceGameAsync_WhenProcessAlreadyExited_ReturnsGracefulSuccessWithoutCallingTerminator()
    {
        using var enforcement = await CreateServiceAsync();

        // Process 5002 is not in _scanner (it already closed on its own)
        var result = await enforcement.EnforceGameAsync(_cs2, 5002, "cs2.exe");

        Assert.True(result.Success);
        Assert.Equal(EnforcementMethod.GracefulClose, result.MethodUsed);
        Assert.Empty(_terminator.Calls);
    }

    [Fact]
    public async Task EnforceGameAsync_WhenTargetPidIsSystem_AbortsAndRefusesToTerminate()
    {
        using var enforcement = await CreateServiceAsync();

        var result = await enforcement.EnforceGameAsync(_cs2, 4, "System");

        Assert.False(result.Success);
        Assert.Equal(EnforcementMethod.Failed, result.MethodUsed);
        Assert.Contains("Protected system PID", result.ErrorMessage);
        Assert.Empty(_terminator.Calls);
    }

    [Fact]
    public async Task EnforceGameAsync_WhenProcessNameMismatched_AbortsDueToReusedPidSafeguard()
    {
        using var enforcement = await CreateServiceAsync();

        // Simulate Windows recycling PID 5003 for a completely different process (e.g. notepad.exe)
        _scanner.AddProcess(5003, "notepad.exe");

        var result = await enforcement.EnforceGameAsync(_cs2, 5003, "cs2.exe");

        Assert.False(result.Success);
        Assert.Equal(EnforcementMethod.Failed, result.MethodUsed);
        Assert.Contains("Reused PID safeguard", result.ErrorMessage);
        Assert.Empty(_terminator.Calls);
    }

    [Fact]
    public async Task EnforceGameAsync_WhenTerminationFails_ReturnsFailedResult()
    {
        using var enforcement = await CreateServiceAsync();

        _scanner.AddProcess(5004, "cs2.exe");
        _terminator.SuccessToReturn = false;
        _terminator.MethodToReturn = EnforcementMethod.Failed;

        var result = await enforcement.EnforceGameAsync(_cs2, 5004, "cs2.exe");

        Assert.False(result.Success);
        Assert.Equal(EnforcementMethod.Failed, result.MethodUsed);
        Assert.Single(_terminator.Calls);
    }

    [Fact]
    public async Task QuotaExhaustedEvent_TriggersAutomaticEnforcementForActiveGameSessions()
    {
        using var enforcement = await CreateServiceAsync();
        await enforcement.StartAsync();

        // Configure a 1-minute limit with 0s grace period for CS2
        await _limitRepo.UpsertLimitAsync(DailyLimit.CreateIndividual(_cs2.Id, 1, gracePeriodSeconds: 0));

        // Launch CS2
        _systemClock.UtcNow = new DateTime(2026, 11, 9, 14, 0, 0, DateTimeKind.Utc);
        _scanner.AddProcess(5005, "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        // Advance time past limit (70 seconds)
        _monotonicClock.Advance(TimeSpan.FromSeconds(70));
        _systemClock.Advance(TimeSpan.FromSeconds(70));

        GameEnforcedEventArgs? enforcedEvent = null;
        enforcement.GameEnforced += (s, e) => enforcedEvent = e;

        // Process monitor heartbeat triggers LimitManager evaluation
        await _processMonitor.CheckProcessesOnceAsync();

        Assert.Single(_terminator.Calls);
        Assert.Equal(5005, _terminator.Calls[0].ProcessId);
        Assert.NotNull(enforcedEvent);
        Assert.Equal(5005, enforcedEvent!.Result.ProcessId);
    }

    [Fact]
    public async Task QuotaExhaustedEvent_WhenNoActiveSessions_DoesNotAttemptTermination()
    {
        using var enforcement = await CreateServiceAsync();
        await enforcement.StartAsync();

        // CS2 has no active sessions running
        Assert.Empty(_timeTracker.GetActiveSessions());

        // Check limits directly with an expired quota
        await _limitRepo.UpsertLimitAsync(DailyLimit.CreateIndividual(_cs2.Id, 1, gracePeriodSeconds: 0));

        // Artificially record an old session that exhausted quota earlier
        await _sessionRepo.StartSessionAsync(new GamingSession
        {
            GameProfileId = _cs2.Id,
            ProcessId = 4999,
            ProcessName = "cs2.exe",
            StartTimeUtc = new DateTime(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 11, 9, 10, 2, 0, DateTimeKind.Utc),
            DurationSeconds = 120,
            TerminationReason = SessionTerminationReason.UserClosed
        });

        await _limitManager.CheckLimitsAsync();

        Assert.Empty(_terminator.Calls);
    }

    [Fact]
    public async Task EnforcementService_StartAndStop_ManagesLifecycleAndSubscriptions()
    {
        using var enforcement = await CreateServiceAsync();

        Assert.False(enforcement.IsRunning);

        await enforcement.StartAsync();
        Assert.True(enforcement.IsRunning);

        await enforcement.StopAsync();
        Assert.False(enforcement.IsRunning);
    }
}

using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using GameGuard.Core.Services;
using GameGuard.Core.Tests.Mocks;
using GameGuard.Infrastructure.Enforcement;
using GameGuard.Infrastructure.Limits;
using GameGuard.Infrastructure.Monitoring;
using GameGuard.Infrastructure.Persistence;
using GameGuard.Infrastructure.Security;
using GameGuard.Infrastructure.Tracking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class EndToEndVerificationTests : IDisposable
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
    private readonly DayBoundaryCalculator _boundaryCalc;
    private readonly LimitCalculator _limitCalc;
    private readonly ClockTamperDetector _tamperDetector;

    private readonly ProcessMonitor _processMonitor;
    private readonly TimeTrackerService _timeTracker;
    private readonly LimitManagerService _limitManager;
    private readonly EnforcementService _enforcementService;
    private readonly RelaunchSuppressionService _relaunchService;

    public EndToEndVerificationTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"gameguard_e2e_{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new SqliteDatabaseInitializer(_connectionFactory, NullLogger<SqliteDatabaseInitializer>.Instance);
        _initializer.InitializeAsync().GetAwaiter().GetResult();

        _gameRepo = new SqliteGameProfileRepository(_connectionFactory);
        _limitRepo = new SqliteLimitRepository(_connectionFactory);
        _sessionRepo = new SqliteSessionRepository(_connectionFactory);
        _overrideRepo = new SqliteAdminOverrideRepository(_connectionFactory);
        _blockRepo = new SqliteRelaunchBlockRepository(_connectionFactory);

        _scanner = new MockProcessScanner();
        _monotonicClock = new MockMonotonicClock();
        _systemClock = new MockSystemClock { UtcNow = new DateTime(2026, 11, 10, 14, 0, 0, DateTimeKind.Utc) };
        _terminator = new MockProcessTerminator();
        _boundaryCalc = new DayBoundaryCalculator();
        _limitCalc = new LimitCalculator();
        _tamperDetector = new ClockTamperDetector(NullLogger<ClockTamperDetector>.Instance, _systemClock.UtcNow);

        _processMonitor = new ProcessMonitor(
            _scanner,
            _gameRepo,
            _systemClock,
            NullLogger<ProcessMonitor>.Instance,
            pollingInterval: TimeSpan.FromMilliseconds(50));

        _timeTracker = new TimeTrackerService(
            _processMonitor,
            _sessionRepo,
            _gameRepo,
            _scanner,
            _systemClock,
            _monotonicClock,
            NullLogger<TimeTrackerService>.Instance,
            checkpointInterval: TimeSpan.FromSeconds(5),
            suspensionGapThreshold: TimeSpan.FromHours(2));

        _limitManager = new LimitManagerService(
            _gameRepo,
            _limitRepo,
            _sessionRepo,
            _timeTracker,
            _limitCalc,
            _boundaryCalc,
            _systemClock,
            NullLogger<LimitManagerService>.Instance,
            evaluationInterval: TimeSpan.FromMilliseconds(50),
            overrideRepository: _overrideRepo,
            clockTamperDetector: _tamperDetector,
            timeZone: TimeZoneInfo.Utc);

        _enforcementService = new EnforcementService(
            _limitManager,
            _timeTracker,
            _gameRepo,
            _sessionRepo,
            _terminator,
            _scanner,
            _systemClock,
            NullLogger<EnforcementService>.Instance);

        _relaunchService = new RelaunchSuppressionService(
            _processMonitor,
            _limitManager,
            _terminator,
            _blockRepo,
            _sessionRepo,
            _timeTracker,
            _systemClock,
            NullLogger<RelaunchSuppressionService>.Instance);
    }

    public void Dispose()
    {
        _relaunchService.Dispose();
        _enforcementService.Dispose();
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

    [Fact]
    public async Task FullGamingLifecycle_FromDetectionToLimitExhaustion_EnforcesAndSuppressesRelaunches()
    {
        // 1. Setup game with 15-minute daily limit and 30-second grace period
        var games = await _gameRepo.GetAllAsync();
        var cs2 = games.First(g => g.ProcessNames.Contains("cs2.exe"));

        await _limitRepo.UpsertLimitAsync(DailyLimit.CreateIndividual(cs2.Id, 15, 30));

        // Start services
        await _timeTracker.InitializeAsync();
        await _processMonitor.StartAsync();
        await _limitManager.StartAsync();
        await _enforcementService.StartAsync();
        await _relaunchService.StartAsync();

        // 2. Game starts (cs2.exe, PID 9001)
        _scanner.AddProcess(9001, "cs2.exe", "C:\\Games\\cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        // Verify active session detected
        var activeSessions = _timeTracker.GetActiveSessions();
        Assert.Single(activeSessions);
        Assert.Equal(9001, activeSessions[0].ProcessId);

        // 3. Play for 10 minutes (within quota)
        _monotonicClock.Advance(TimeSpan.FromMinutes(10));
        _systemClock.Advance(TimeSpan.FromMinutes(10));
        await _processMonitor.CheckProcessesOnceAsync();
        await _limitManager.CheckLimitsAsync();

        var statusMid = await _limitManager.GetCurrentStatusAsync(cs2.Id);
        Assert.False(statusMid.IsLimitExceeded);
        Assert.False(statusMid.IsInGracePeriod);
        Assert.Equal(5, statusMid.MinutesRemaining);

        // 4. Play for another 5 minutes (quota reaches 15 minutes -> triggers grace period)
        _monotonicClock.Advance(TimeSpan.FromMinutes(5));
        _systemClock.Advance(TimeSpan.FromMinutes(5));
        await _processMonitor.CheckProcessesOnceAsync();
        await _limitManager.CheckLimitsAsync();

        var statusGrace = await _limitManager.GetCurrentStatusAsync(cs2.Id);
        Assert.True(statusGrace.IsInGracePeriod);

        // 5. Grace period expires (advance 35 seconds -> quota exhausted)
        _monotonicClock.Advance(TimeSpan.FromSeconds(35));
        _systemClock.Advance(TimeSpan.FromSeconds(35));
        await _processMonitor.CheckProcessesOnceAsync();
        await _limitManager.CheckLimitsAsync();

        // Process terminator must have been called to terminate PID 9001
        Assert.Contains(_terminator.Calls, c => c.ProcessId == 9001);

        // Simulate process termination in scanner
        _scanner.Clear();
        await _processMonitor.CheckProcessesOnceAsync();

        // Verify session recorded as LimitEnforced in SQLite
        var recentSessions = await _sessionRepo.GetRecentSessionsAsync(5);
        Assert.Single(recentSessions);
        Assert.Equal(SessionTerminationReason.LimitEnforced, recentSessions[0].TerminationReason);

        // 6. Gamer attempts immediate relaunch while limit is exhausted
        _scanner.AddProcess(9002, "cs2.exe", "C:\\Games\\cs2.exe");

        // Relaunch suppression intercepts the process immediately
        bool wasSuppressed = await _relaunchService.CheckAndSuppressIfExhaustedAsync(cs2, 9002, "cs2.exe");
        Assert.True(wasSuppressed);
        Assert.Contains(_terminator.Calls, c => c.ProcessId == 9002);

        // Verify block recorded in audit repository
        var blocks = await _blockRepo.GetRecentBlocksAsync(5);
        Assert.Single(blocks);
        Assert.Equal(9002, blocks[0].ProcessId);

        // 7. Admin grants +15m bonus time override
        var bonus = AdminOverride.CreateBonusTime(cs2.Id, 15, _systemClock.UtcNow, TimeSpan.FromHours(2), "Admin approved bonus");
        await _overrideRepo.AddOverrideAsync(bonus);

        // 8. Relaunch is now permitted with bonus time
        _scanner.Clear();
        _scanner.AddProcess(9003, "cs2.exe", "C:\\Games\\cs2.exe");
        bool suppressedAfterBonus = await _relaunchService.CheckAndSuppressIfExhaustedAsync(cs2, 9003, "cs2.exe");
        Assert.False(suppressedAfterBonus);
    }

    [Fact]
    public async Task MidnightDayRollover_MidSession_ResetsDailyAllowanceWithoutDataLoss()
    {
        var games = await _gameRepo.GetAllAsync();
        var cs2 = games.First(g => g.ProcessNames.Contains("cs2.exe"));

        // Set 60-minute daily limit, reset hour = 0 UTC
        await _limitRepo.UpsertLimitAsync(DailyLimit.CreateIndividual(cs2.Id, 60, 30));

        // Start playing at 23:30 on Day 1
        _systemClock.UtcNow = new DateTime(2026, 11, 10, 23, 30, 0, DateTimeKind.Utc);

        await _timeTracker.InitializeAsync();
        await _processMonitor.StartAsync();
        await _limitManager.StartAsync();

        _scanner.AddProcess(8001, "cs2.exe", "C:\\Games\\cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        // Play for 20 minutes (until 23:50)
        _monotonicClock.Advance(TimeSpan.FromMinutes(20));
        _systemClock.Advance(TimeSpan.FromMinutes(20));
        await _processMonitor.CheckProcessesOnceAsync();
        await _limitManager.CheckLimitsAsync();

        var statusBeforeMidnight = await _limitManager.GetCurrentStatusAsync(cs2.Id);
        Assert.Equal(20, statusBeforeMidnight.MinutesUsedToday);
        Assert.Equal(40, statusBeforeMidnight.MinutesRemaining);

        // Cross midnight boundary (advance to 00:15 on Day 2)
        _monotonicClock.Advance(TimeSpan.FromMinutes(25));
        _systemClock.Advance(TimeSpan.FromMinutes(25)); // Now 00:15 Nov 11
        await _processMonitor.CheckProcessesOnceAsync();
        await _limitManager.CheckLimitsAsync();

        // On Day 2, usage resets: only the 15 minutes played after midnight count towards Day 2 quota
        var statusAfterMidnight = await _limitManager.GetCurrentStatusAsync(cs2.Id);
        Assert.True(statusAfterMidnight.MinutesUsedToday <= 15);
        Assert.True(statusAfterMidnight.MinutesRemaining >= 45);
        Assert.False(statusAfterMidnight.IsLimitExceeded);
    }
}

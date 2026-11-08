using GameGuard.Core.Models;
using GameGuard.Core.Tests.Mocks;
using GameGuard.Infrastructure.Monitoring;
using GameGuard.Infrastructure.Persistence;
using GameGuard.Infrastructure.Tracking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class TimeTrackerServiceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteDatabaseInitializer _initializer;
    private readonly SqliteGameProfileRepository _gameRepo;
    private readonly SqliteSessionRepository _sessionRepo;
    private readonly MockProcessScanner _scanner;
    private readonly MockMonotonicClock _monotonicClock;
    private readonly MockSystemClock _systemClock;
    private readonly ProcessMonitor _processMonitor;

    private readonly GameProfile _cs2 = new()
    {
        Id = Guid.NewGuid(),
        Name = "Counter-Strike 2",
        ProcessNames = new[] { "cs2.exe" },
        IsMonitored = true
    };

    private readonly GameProfile _valorant = new()
    {
        Id = Guid.NewGuid(),
        Name = "Valorant",
        ProcessNames = new[] { "VALORANT-Win64-Shipping.exe" },
        IsMonitored = true
    };

    public TimeTrackerServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"gameguard_tracker_{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new SqliteDatabaseInitializer(_connectionFactory, NullLogger<SqliteDatabaseInitializer>.Instance);
        _gameRepo = new SqliteGameProfileRepository(_connectionFactory);
        _sessionRepo = new SqliteSessionRepository(_connectionFactory);
        _scanner = new MockProcessScanner();
        _monotonicClock = new MockMonotonicClock();
        _systemClock = new MockSystemClock();

        _processMonitor = new ProcessMonitor(
            _scanner,
            _gameRepo,
            _systemClock,
            NullLogger<ProcessMonitor>.Instance);
    }

    public void Dispose()
    {
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
            // Ignore lock during temp file cleanup
        }
    }

    private async Task<TimeTrackerService> CreateTrackerAsync(TimeSpan? checkpointInterval = null, TimeSpan? suspensionThreshold = null)
    {
        await _initializer.InitializeAsync();
        await _gameRepo.AddAsync(_cs2);
        await _gameRepo.AddAsync(_valorant);

        return new TimeTrackerService(
            _processMonitor,
            _sessionRepo,
            _gameRepo,
            _scanner,
            _systemClock,
            _monotonicClock,
            NullLogger<TimeTrackerService>.Instance,
            checkpointInterval: checkpointInterval ?? TimeSpan.FromSeconds(15),
            suspensionGapThreshold: suspensionThreshold ?? TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task Lifecycle_StartingMonitoredGame_CreatesSessionInDatabaseAndMemory()
    {
        await using var tracker = await CreateTrackerAsync();

        SessionLifecycleEventArgs? startedArgs = null;
        tracker.SessionStarted += (_, args) => startedArgs = args;

        _scanner.AddProcess(pid: 2001, name: "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        Assert.NotNull(startedArgs);
        Assert.Equal("Counter-Strike 2", startedArgs.Game.Name);
        Assert.Equal(2001, startedArgs.Session.ProcessId);

        var activeSessions = tracker.GetActiveSessions();
        Assert.Single(activeSessions);
        Assert.Equal(startedArgs.Session.Id, activeSessions[0].Id);

        var dbSession = await _sessionRepo.GetActiveSessionByProcessIdAsync(2001);
        Assert.NotNull(dbSession);
        Assert.Equal(startedArgs.Session.Id, dbSession.Id);
        Assert.Equal(0, dbSession.DurationSeconds);
    }

    [Fact]
    public async Task Lifecycle_RunningGame_UpdatesSessionDuration()
    {
        await using var tracker = await CreateTrackerAsync(checkpointInterval: TimeSpan.FromSeconds(5));

        _scanner.AddProcess(pid: 2001, name: "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        // Advance monotonic time by 5 seconds
        _monotonicClock.Advance(TimeSpan.FromSeconds(5));
        _systemClock.Advance(TimeSpan.FromSeconds(5));

        await _processMonitor.CheckProcessesOnceAsync();

        var active = tracker.GetActiveSession(2001);
        Assert.NotNull(active);
        Assert.Equal(5, active.DurationSeconds);

        var dbSession = await _sessionRepo.GetActiveSessionByProcessIdAsync(2001);
        Assert.NotNull(dbSession);
        Assert.Equal(5, dbSession.DurationSeconds);
    }

    [Fact]
    public async Task Lifecycle_ExitingGame_FinalizesSessionInDatabaseAndMemory()
    {
        await using var tracker = await CreateTrackerAsync(suspensionThreshold: TimeSpan.FromSeconds(60));

        SessionLifecycleEventArgs? endedArgs = null;
        tracker.SessionEnded += (_, args) => endedArgs = args;

        _scanner.AddProcess(pid: 2001, name: "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        _monotonicClock.Advance(TimeSpan.FromSeconds(12));
        _systemClock.Advance(TimeSpan.FromSeconds(12));

        // Process exits
        _scanner.RemoveProcess(2001);
        await _processMonitor.CheckProcessesOnceAsync();

        Assert.NotNull(endedArgs);
        Assert.Equal(12, endedArgs.Session.DurationSeconds);
        Assert.Equal(SessionTerminationReason.UserClosed, endedArgs.TerminationReason);

        Assert.Empty(tracker.GetActiveSessions());

        // Verify database no longer has active session for PID 2001
        var dbActive = await _sessionRepo.GetActiveSessionByProcessIdAsync(2001);
        Assert.Null(dbActive);

        var recent = await _sessionRepo.GetRecentSessionsAsync(5);
        var closedSession = recent.FirstOrDefault(s => s.Id == endedArgs.Session.Id);
        Assert.NotNull(closedSession);
        Assert.NotNull(closedSession.EndTimeUtc);
        Assert.Equal(12, closedSession.DurationSeconds);
        Assert.Equal(SessionTerminationReason.UserClosed, closedSession.TerminationReason);
    }

    [Fact]
    public async Task Lifecycle_DuplicateProcessStartedEvent_DoesNotCreateDuplicateSession()
    {
        await using var tracker = await CreateTrackerAsync();

        _scanner.AddProcess(pid: 2001, name: "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        // Subsequent scan with same process
        await _processMonitor.CheckProcessesOnceAsync();

        var active = tracker.GetActiveSessions();
        Assert.Single(active);

        var dbSessions = await _sessionRepo.GetActiveSessionsAsync();
        Assert.Single(dbSessions);
    }

    [Fact]
    public async Task Lifecycle_UnmonitoredProcess_DoesNotCreateSession()
    {
        await using var tracker = await CreateTrackerAsync();

        _scanner.AddProcess(pid: 3001, name: "notepad.exe");
        _scanner.AddProcess(pid: 3002, name: "chrome.exe");

        await _processMonitor.CheckProcessesOnceAsync();

        Assert.Empty(tracker.GetActiveSessions());
        Assert.Empty(await _sessionRepo.GetActiveSessionsAsync());
    }

    [Fact]
    public async Task TimeTracking_PeriodicHeartbeat_PersistsAtConfiguredInterval()
    {
        // Checkpoint interval = 10 seconds
        await using var tracker = await CreateTrackerAsync(checkpointInterval: TimeSpan.FromSeconds(10));

        _scanner.AddProcess(pid: 2001, name: "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        // Advance 4 seconds (below checkpoint threshold)
        _monotonicClock.Advance(TimeSpan.FromSeconds(4));
        await _processMonitor.CheckProcessesOnceAsync();

        // In memory it is 4 seconds, but in DB it should still be 0 seconds (uncheckpointed)
        var memSession = tracker.GetActiveSession(2001);
        Assert.Equal(4, memSession!.DurationSeconds);

        var dbSession = await _sessionRepo.GetActiveSessionByProcessIdAsync(2001);
        Assert.Equal(0, dbSession!.DurationSeconds);

        // Advance another 6 seconds (total 10 seconds, hits checkpoint!)
        _monotonicClock.Advance(TimeSpan.FromSeconds(6));
        await _processMonitor.CheckProcessesOnceAsync();

        dbSession = await _sessionRepo.GetActiveSessionByProcessIdAsync(2001);
        Assert.Equal(10, dbSession!.DurationSeconds);
    }

    [Fact]
    public async Task TimeTracking_Exit_PersistsFinalDurationEvenIfCheckpointNotReached()
    {
        // Checkpoint interval = 30 seconds
        await using var tracker = await CreateTrackerAsync(checkpointInterval: TimeSpan.FromSeconds(30), suspensionThreshold: TimeSpan.FromSeconds(60));

        _scanner.AddProcess(pid: 2001, name: "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        // Run for 7 seconds (far less than 30s checkpoint)
        _monotonicClock.Advance(TimeSpan.FromSeconds(7));
        _scanner.RemoveProcess(2001);

        await _processMonitor.CheckProcessesOnceAsync();

        var recent = await _sessionRepo.GetRecentSessionsAsync(1);
        Assert.Single(recent);
        Assert.Equal(7, recent[0].DurationSeconds);
    }

    [Fact]
    public async Task TimeTracking_SystemClockJump_DoesNotCorruptMonotonicElapsedCalculations()
    {
        await using var tracker = await CreateTrackerAsync(checkpointInterval: TimeSpan.FromSeconds(5));

        _scanner.AddProcess(pid: 2001, name: "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        // Simulate wall clock jumping backwards 2 hours (e.g. NTP sync / Daylight Saving)
        _systemClock.Advance(TimeSpan.FromHours(-2));

        // Monotonic clock advances by exactly 10 seconds
        _monotonicClock.Advance(TimeSpan.FromSeconds(10));

        await _processMonitor.CheckProcessesOnceAsync();

        var session = tracker.GetActiveSession(2001);
        Assert.NotNull(session);
        // Duration must remain exactly 10 seconds, completely unaffected by wall clock jump!
        Assert.Equal(10, session.DurationSeconds);
    }

    [Fact]
    public async Task TimeTracking_SleepOrSuspensionGap_IsClampedToAvoidInflatedPlaytime()
    {
        // Suspension threshold = 10s
        await using var tracker = await CreateTrackerAsync(suspensionThreshold: TimeSpan.FromSeconds(10));

        _scanner.AddProcess(pid: 2001, name: "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        // Laptop goes to sleep for 4 hours (14400 seconds)
        _monotonicClock.Advance(TimeSpan.FromHours(4));
        _systemClock.Advance(TimeSpan.FromHours(4));

        await _processMonitor.CheckProcessesOnceAsync();

        var session = tracker.GetActiveSession(2001);
        Assert.NotNull(session);
        // The 4-hour gap is clamped to 1 second, not 14,400 seconds!
        Assert.Equal(1, session.DurationSeconds);
    }

    [Fact]
    public async Task Recovery_ValidActiveSession_IsRecoveredOnStartup()
    {
        await _initializer.InitializeAsync();
        await _gameRepo.AddAsync(_cs2);

        // Existing session in database (e.g. GameGuard crashed earlier while CS2 was running)
        var orphanSession = new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = _cs2.Id,
            ProcessId = 4444,
            ProcessName = "cs2.exe",
            StartTimeUtc = DateTime.UtcNow.AddMinutes(-20),
            DurationSeconds = 1200,
            TerminationReason = SessionTerminationReason.Active
        };
        await _sessionRepo.StartSessionAsync(orphanSession);

        // System process scanner reports PID 4444 cs2.exe is still running!
        _scanner.AddProcess(pid: 4444, name: "cs2.exe");

        await using var tracker = new TimeTrackerService(
            _processMonitor,
            _sessionRepo,
            _gameRepo,
            _scanner,
            _systemClock,
            _monotonicClock,
            NullLogger<TimeTrackerService>.Instance,
            suspensionGapThreshold: TimeSpan.FromSeconds(60));

        // Run recovery
        await tracker.InitializeAsync();

        var recovered = tracker.GetActiveSession(4444);
        Assert.NotNull(recovered);
        Assert.Equal(orphanSession.Id, recovered.Id);
        Assert.Equal(1200, recovered.DurationSeconds);

        // Continue running
        _monotonicClock.Advance(TimeSpan.FromSeconds(10));
        await _processMonitor.CheckProcessesOnceAsync();

        Assert.Equal(1210, recovered.DurationSeconds);
    }

    [Fact]
    public async Task Recovery_StaleSession_IsFinalizedOnStartup()
    {
        await _initializer.InitializeAsync();
        await _gameRepo.AddAsync(_cs2);

        // Stale session whose game is NO LONGER running
        var staleSession = new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = _cs2.Id,
            ProcessId = 5555,
            ProcessName = "cs2.exe",
            StartTimeUtc = DateTime.UtcNow.AddMinutes(-30),
            DurationSeconds = 600,
            TerminationReason = SessionTerminationReason.Active
        };
        await _sessionRepo.StartSessionAsync(staleSession);

        // Process scanner does NOT contain PID 5555
        _scanner.Clear();

        await using var tracker = new TimeTrackerService(
            _processMonitor,
            _sessionRepo,
            _gameRepo,
            _scanner,
            _systemClock,
            _monotonicClock,
            NullLogger<TimeTrackerService>.Instance);

        await tracker.InitializeAsync();

        // Not in active tracking
        Assert.Null(tracker.GetActiveSession(5555));

        // In database, session must now be marked ended with CrashOrExternal
        var dbActive = await _sessionRepo.GetActiveSessionByProcessIdAsync(5555);
        Assert.Null(dbActive);

        var recent = await _sessionRepo.GetRecentSessionsAsync(5);
        var finalized = recent.First(s => s.Id == staleSession.Id);
        Assert.NotNull(finalized.EndTimeUtc);
        Assert.Equal(SessionTerminationReason.CrashOrExternal, finalized.TerminationReason);
    }

    [Fact]
    public async Task Recovery_ReusedPidWithDifferentProcessName_IsRecognizedAsStaleAndFinalized()
    {
        await _initializer.InitializeAsync();
        await _gameRepo.AddAsync(_cs2);

        // DB session was cs2.exe on PID 7777
        var staleSession = new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = _cs2.Id,
            ProcessId = 7777,
            ProcessName = "cs2.exe",
            StartTimeUtc = DateTime.UtcNow.AddMinutes(-10),
            DurationSeconds = 300,
            TerminationReason = SessionTerminationReason.Active
        };
        await _sessionRepo.StartSessionAsync(staleSession);

        // But PID 7777 now belongs to svchost.exe! (Windows PID reuse)
        _scanner.AddProcess(pid: 7777, name: "svchost.exe");

        await using var tracker = new TimeTrackerService(
            _processMonitor,
            _sessionRepo,
            _gameRepo,
            _scanner,
            _systemClock,
            _monotonicClock,
            NullLogger<TimeTrackerService>.Instance);

        await tracker.InitializeAsync();

        // Must NOT match svchost.exe as CS2!
        Assert.Null(tracker.GetActiveSession(7777));

        var dbActive = await _sessionRepo.GetActiveSessionByProcessIdAsync(7777);
        Assert.Null(dbActive);
    }

    [Fact]
    public async Task Concurrency_MultipleGamesSimultaneously_AreTrackedIndependently()
    {
        await using var tracker = await CreateTrackerAsync(checkpointInterval: TimeSpan.FromSeconds(5), suspensionThreshold: TimeSpan.FromSeconds(60));

        // Start CS2 and Valorant simultaneously
        _scanner.AddProcess(pid: 101, name: "cs2.exe");
        _scanner.AddProcess(pid: 102, name: "VALORANT-Win64-Shipping.exe");

        await _processMonitor.CheckProcessesOnceAsync();

        var active = tracker.GetActiveSessions();
        Assert.Equal(2, active.Count);

        // Advance 15 seconds
        _monotonicClock.Advance(TimeSpan.FromSeconds(15));
        await _processMonitor.CheckProcessesOnceAsync();

        var cs2Session = tracker.GetActiveSession(101);
        var valSession = tracker.GetActiveSession(102);
        Assert.NotNull(cs2Session);
        Assert.NotNull(valSession);
        Assert.Equal(15, cs2Session.DurationSeconds);
        Assert.Equal(15, valSession.DurationSeconds);

        // Exit CS2, keep Valorant running
        _scanner.RemoveProcess(101);
        _monotonicClock.Advance(TimeSpan.FromSeconds(10));

        await _processMonitor.CheckProcessesOnceAsync();

        Assert.Null(tracker.GetActiveSession(101));
        var valRemaining = tracker.GetActiveSession(102);
        Assert.NotNull(valRemaining);
        Assert.Equal(25, valRemaining.DurationSeconds);
    }

    [Fact]
    public async Task TimeTracking_LongRunningSession_MaintainsAccuracy()
    {
        // Checkpoint every 30s, suspension threshold 60s
        await using var tracker = await CreateTrackerAsync(checkpointInterval: TimeSpan.FromSeconds(30), suspensionThreshold: TimeSpan.FromSeconds(60));

        _scanner.AddProcess(pid: 2001, name: "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        // Simulate 10 hours of gameplay in 10-second ticks (3600 cycles)
        for (int i = 0; i < 3600; i++)
        {
            _monotonicClock.Advance(TimeSpan.FromSeconds(10));
            _systemClock.Advance(TimeSpan.FromSeconds(10));
            await _processMonitor.CheckProcessesOnceAsync();
        }

        var active = tracker.GetActiveSession(2001);
        Assert.NotNull(active);
        Assert.Equal(36000, active.DurationSeconds); // Exactly 10 hours

        var dbSession = await _sessionRepo.GetActiveSessionByProcessIdAsync(2001);
        Assert.NotNull(dbSession);
        Assert.Equal(36000, dbSession.DurationSeconds);
    }

    [Fact]
    public async Task Recovery_DoesNotCreateDuplicateSessions_WhenProcessScannedMultipleTimes()
    {
        await _initializer.InitializeAsync();
        await _gameRepo.AddAsync(_cs2);

        var existingSession = new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = _cs2.Id,
            ProcessId = 6666,
            ProcessName = "cs2.exe",
            StartTimeUtc = DateTime.UtcNow.AddMinutes(-15),
            DurationSeconds = 900,
            TerminationReason = SessionTerminationReason.Active
        };
        await _sessionRepo.StartSessionAsync(existingSession);

        _scanner.AddProcess(pid: 6666, name: "cs2.exe");

        await using var tracker = new TimeTrackerService(
            _processMonitor,
            _sessionRepo,
            _gameRepo,
            _scanner,
            _systemClock,
            _monotonicClock,
            NullLogger<TimeTrackerService>.Instance,
            suspensionGapThreshold: TimeSpan.FromSeconds(60));

        await tracker.InitializeAsync();

        // Multiple monitor scans after recovery
        await _processMonitor.CheckProcessesOnceAsync();
        await _processMonitor.CheckProcessesOnceAsync();
        await _processMonitor.CheckProcessesOnceAsync();

        var activeSessions = tracker.GetActiveSessions();
        Assert.Single(activeSessions);
        Assert.Equal(existingSession.Id, activeSessions[0].Id);

        var allActiveInDb = await _sessionRepo.GetActiveSessionsAsync();
        Assert.Single(allActiveInDb);
    }

    [Fact]
    public async Task Concurrency_ConcurrentLifecycleEvents_MaintainConsistency()
    {
        await using var tracker = await CreateTrackerAsync(checkpointInterval: TimeSpan.FromSeconds(5), suspensionThreshold: TimeSpan.FromSeconds(60));

        // Create 10 distinct game profiles and processes
        var profiles = new List<GameProfile>();
        for (int i = 0; i < 10; i++)
        {
            var p = new GameProfile
            {
                Id = Guid.NewGuid(),
                Name = $"CustomGame_{i}",
                ProcessNames = new[] { $"game_{i}.exe" },
                IsMonitored = true
            };
            profiles.Add(p);
            await _gameRepo.AddAsync(p);
            _scanner.AddProcess(pid: 8000 + i, name: $"game_{i}.exe");
        }

        // Start all 10 processes
        await _processMonitor.CheckProcessesOnceAsync();

        Assert.Equal(10, tracker.GetActiveSessions().Count);

        // Run 5 cycles of concurrent updates
        for (int cycle = 0; cycle < 5; cycle++)
        {
            _monotonicClock.Advance(TimeSpan.FromSeconds(5));
            await _processMonitor.CheckProcessesOnceAsync();
        }

        var sessions = tracker.GetActiveSessions();
        Assert.Equal(10, sessions.Count);
        Assert.All(sessions, s => Assert.Equal(25, s.DurationSeconds));

        // Exit half of the games
        for (int i = 0; i < 5; i++)
        {
            _scanner.RemoveProcess(8000 + i);
        }

        await _processMonitor.CheckProcessesOnceAsync();

        Assert.Equal(5, tracker.GetActiveSessions().Count);
        var activeInDb = await _sessionRepo.GetActiveSessionsAsync();
        Assert.Equal(5, activeInDb.Count);
    }
}

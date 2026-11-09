using GameGuard.Core.Models;
using GameGuard.Core.Services;
using GameGuard.Core.Tests.Mocks;
using GameGuard.Infrastructure.Limits;
using GameGuard.Infrastructure.Monitoring;
using GameGuard.Infrastructure.Persistence;
using GameGuard.Infrastructure.Tracking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class LimitManagerServiceTests : IDisposable
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
    private readonly ProcessMonitor _processMonitor;
    private readonly TimeTrackerService _timeTracker;
    private readonly LimitCalculator _limitCalculator;
    private readonly DayBoundaryCalculator _boundaryCalculator;

    private GameProfile _cs2 = null!;
    private GameProfile _valorant = null!;

    public LimitManagerServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"gameguard_limits_{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new SqliteDatabaseInitializer(_connectionFactory, NullLogger<SqliteDatabaseInitializer>.Instance);
        _gameRepo = new SqliteGameProfileRepository(_connectionFactory);
        _limitRepo = new SqliteLimitRepository(_connectionFactory);
        _sessionRepo = new SqliteSessionRepository(_connectionFactory);
        _scanner = new MockProcessScanner();
        _monotonicClock = new MockMonotonicClock();
        _systemClock = new MockSystemClock();

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
    }

    public void Dispose()
    {
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

    private async Task<LimitManagerService> CreateServiceAsync(IEnumerable<int>? warningThresholds = null)
    {
        await _initializer.InitializeAsync();
        var allGames = await _gameRepo.GetAllAsync();
        _cs2 = allGames.First(g => g.Name == "Counter-Strike 2");
        _valorant = allGames.First(g => g.Name == "Valorant");

        return new LimitManagerService(
            _gameRepo,
            _limitRepo,
            _sessionRepo,
            _timeTracker,
            _limitCalculator,
            _boundaryCalculator,
            _systemClock,
            NullLogger<LimitManagerService>.Instance,
            timeZone: TimeZoneInfo.Utc,
            warningThresholdsMinutes: warningThresholds ?? new[] { 15, 5, 1 },
            evaluationInterval: TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task GetCurrentStatus_WithIndividualLimitOnly_CalculatesUsageAndRemainingCorrectly()
    {
        await using var service = await CreateServiceAsync();

        // 60-minute limit on CS2
        await _limitRepo.UpsertLimitAsync(DailyLimit.CreateIndividual(_cs2.Id, minutes: 60));

        // 20 minutes played earlier today (1200 seconds)
        var priorSession = new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = _cs2.Id,
            ProcessId = 1111,
            ProcessName = "cs2.exe",
            StartTimeUtc = _systemClock.UtcNow.AddHours(-2),
            EndTimeUtc = _systemClock.UtcNow.AddHours(-2).AddMinutes(20),
            DurationSeconds = 1200,
            TerminationReason = SessionTerminationReason.UserClosed
        };
        await _sessionRepo.StartSessionAsync(priorSession);
        await _sessionRepo.EndSessionAsync(priorSession.Id, priorSession.EndTimeUtc.Value, SessionTerminationReason.UserClosed, durationSeconds: 1200);

        // Start CS2 now and run for 10 minutes (600s)
        _scanner.AddProcess(pid: 2222, name: "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        _monotonicClock.Advance(TimeSpan.FromMinutes(10));
        _systemClock.Advance(TimeSpan.FromMinutes(10));
        await _processMonitor.CheckProcessesOnceAsync();

        var status = await service.GetCurrentStatusAsync(_cs2.Id);

        Assert.Equal(_cs2.Id, status.GameProfileId);
        Assert.True(status.HasLimit);
        Assert.Equal(60, status.EffectiveLimitMinutes);
        Assert.Equal(30, status.MinutesUsedToday); // 20m prior + 10m active = 30m
        Assert.Equal(1800, status.SecondsRemaining); // 1800s = 30m remaining
        Assert.Equal(30, status.MinutesRemaining);
        Assert.False(status.IsLimitExceeded);
        Assert.False(status.IsInGracePeriod);
    }

    [Fact]
    public async Task GetCurrentStatus_WithSharedLimitOnly_AggregatesAcrossAllGames()
    {
        await using var service = await CreateServiceAsync();

        // 90-minute shared limit across all games
        await _limitRepo.UpsertLimitAsync(DailyLimit.CreateShared(minutes: 90));

        // Valorant played for 45 minutes earlier today
        var valSession = new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = _valorant.Id,
            ProcessId = 3333,
            ProcessName = "VALORANT-Win64-Shipping.exe",
            StartTimeUtc = _systemClock.UtcNow.AddHours(-3),
            EndTimeUtc = _systemClock.UtcNow.AddHours(-3).AddMinutes(45),
            DurationSeconds = 2700,
            TerminationReason = SessionTerminationReason.UserClosed
        };
        await _sessionRepo.StartSessionAsync(valSession);
        await _sessionRepo.EndSessionAsync(valSession.Id, valSession.EndTimeUtc.Value, SessionTerminationReason.UserClosed, durationSeconds: 2700);

        // CS2 launched and running for 15 minutes
        _scanner.AddProcess(pid: 4444, name: "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        _monotonicClock.Advance(TimeSpan.FromMinutes(15));
        _systemClock.Advance(TimeSpan.FromMinutes(15));
        await _processMonitor.CheckProcessesOnceAsync();

        var status = await service.GetCurrentStatusAsync(_cs2.Id);

        Assert.True(status.HasLimit);
        Assert.True(status.IsSharedLimitBinding);
        Assert.Equal(90, status.EffectiveLimitMinutes);
        Assert.Equal(1800, status.SecondsRemaining); // 90m allowed - (45m Val + 15m CS2) = 30m remaining (1800s)
        Assert.Equal(30, status.MinutesRemaining);
    }

    [Fact]
    public async Task GetCurrentStatus_WithDualLimits_IdentifiesBindingConstraint()
    {
        await using var service = await CreateServiceAsync();

        // CS2 individual limit = 120 minutes, Shared limit = 60 minutes
        await _limitRepo.UpsertLimitAsync(DailyLimit.CreateIndividual(_cs2.Id, minutes: 120));
        await _limitRepo.UpsertLimitAsync(DailyLimit.CreateShared(minutes: 60));

        var status = await service.GetCurrentStatusAsync(_cs2.Id);

        Assert.True(status.HasLimit);
        Assert.True(status.IsSharedLimitBinding);
        Assert.Equal(60, status.EffectiveLimitMinutes);
    }

    [Fact]
    public async Task CheckLimits_WhenApproachingThresholds_FiresLimitWarningIssuedOncePerThreshold()
    {
        await using var service = await CreateServiceAsync(warningThresholds: new[] { 15, 5, 1 });

        await _limitRepo.UpsertLimitAsync(DailyLimit.CreateIndividual(_cs2.Id, minutes: 60));

        var warningsFired = new List<int>();
        service.LimitWarningIssued += (_, args) => warningsFired.Add(args.WarningThresholdMinutes);

        // Start CS2
        _scanner.AddProcess(pid: 2001, name: "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        // Advance 46 minutes (14 minutes remaining -> crosses 15m threshold)
        _monotonicClock.Advance(TimeSpan.FromMinutes(46));
        _systemClock.Advance(TimeSpan.FromMinutes(46));
        await _processMonitor.CheckProcessesOnceAsync();

        await service.CheckLimitsAsync();

        Assert.Single(warningsFired);
        Assert.Equal(15, warningsFired[0]);

        // Subsequent check at 47 minutes (13 minutes remaining): 15m threshold must NOT refire
        _monotonicClock.Advance(TimeSpan.FromMinutes(1));
        _systemClock.Advance(TimeSpan.FromMinutes(1));
        await _processMonitor.CheckProcessesOnceAsync();

        await service.CheckLimitsAsync();
        Assert.Single(warningsFired);

        // Advance to 56 minutes (4 minutes remaining -> crosses 5m threshold)
        _monotonicClock.Advance(TimeSpan.FromMinutes(9));
        _systemClock.Advance(TimeSpan.FromMinutes(9));
        await _processMonitor.CheckProcessesOnceAsync();

        await service.CheckLimitsAsync();

        Assert.Equal(2, warningsFired.Count);
        Assert.Equal(5, warningsFired[1]);
    }

    [Fact]
    public async Task CheckLimits_WhenTimeExpires_FiresGracePeriodStartedAndUpdated()
    {
        await using var service = await CreateServiceAsync();

        // 60-minute limit with 60s grace period
        await _limitRepo.UpsertLimitAsync(DailyLimit.CreateIndividual(_cs2.Id, minutes: 60, gracePeriodSeconds: 60));

        int graceStarted = 0;
        int graceUpdated = 0;
        service.GracePeriodStarted += (_, _) => graceStarted++;
        service.GracePeriodUpdated += (_, _) => graceUpdated++;

        _scanner.AddProcess(pid: 2001, name: "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        // 60 minutes and 15 seconds elapsed (15 seconds into grace period)
        _monotonicClock.Advance(TimeSpan.FromMinutes(60).Add(TimeSpan.FromSeconds(15)));
        _systemClock.Advance(TimeSpan.FromMinutes(60).Add(TimeSpan.FromSeconds(15)));
        await _processMonitor.CheckProcessesOnceAsync();

        Assert.Equal(1, graceStarted);
        Assert.Equal(1, graceUpdated);

        // Advance another 10 seconds into grace period (25s total into grace)
        _monotonicClock.Advance(TimeSpan.FromSeconds(10));
        _systemClock.Advance(TimeSpan.FromSeconds(10));
        await _processMonitor.CheckProcessesOnceAsync();

        // Started must not refire, but Updated should fire again!
        Assert.Equal(1, graceStarted);
        Assert.Equal(2, graceUpdated);
    }

    [Fact]
    public async Task CheckLimits_WhenGracePeriodExhausted_FiresQuotaExhausted()
    {
        await using var service = await CreateServiceAsync();

        // 60-minute limit, 60s grace
        await _limitRepo.UpsertLimitAsync(DailyLimit.CreateIndividual(_cs2.Id, minutes: 60, gracePeriodSeconds: 60));

        QuotaExhaustedEventArgs? exhaustedArgs = null;
        service.QuotaExhausted += (_, args) => exhaustedArgs = args;

        _scanner.AddProcess(pid: 2001, name: "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();

        // 60 minutes + 65 seconds elapsed (grace period exhausted!)
        _monotonicClock.Advance(TimeSpan.FromMinutes(60).Add(TimeSpan.FromSeconds(65)));
        _systemClock.Advance(TimeSpan.FromMinutes(60).Add(TimeSpan.FromSeconds(65)));
        await _processMonitor.CheckProcessesOnceAsync();

        await service.CheckLimitsAsync();

        Assert.NotNull(exhaustedArgs);
        Assert.Equal("Counter-Strike 2", exhaustedArgs.Game.Name);
        Assert.True(exhaustedArgs.CalculationResult.IsLimitExceeded);
        Assert.False(exhaustedArgs.CalculationResult.IsInGracePeriod);
    }

    [Fact]
    public async Task CheckLimits_WhenDayRolloverOccurs_FiresDayRolloverOccurredAndResetsWarnings()
    {
        await using var service = await CreateServiceAsync(warningThresholds: new[] { 15 });

        await _limitRepo.UpsertLimitAsync(DailyLimit.CreateIndividual(_cs2.Id, minutes: 60));

        DayRolloverEventArgs? rolloverArgs = null;
        service.DayRolloverOccurred += (_, args) => rolloverArgs = args;

        var warnings = new List<int>();
        service.LimitWarningIssued += (_, args) => warnings.Add(args.WarningThresholdMinutes);

        // Initial check initializes the last known day
        await service.CheckLimitsAsync();

        // Fire 15m warning today
        _scanner.AddProcess(pid: 2001, name: "cs2.exe");
        await _processMonitor.CheckProcessesOnceAsync();
        _monotonicClock.Advance(TimeSpan.FromMinutes(46));
        _systemClock.Advance(TimeSpan.FromMinutes(46));
        await _processMonitor.CheckProcessesOnceAsync();
        await service.CheckLimitsAsync();

        Assert.Single(warnings);

        // Exit process
        _scanner.RemoveProcess(2001);
        await _processMonitor.CheckProcessesOnceAsync();

        // Next calendar day arrives (advance system clock by 24 hours)
        _systemClock.Advance(TimeSpan.FromHours(24));

        await service.CheckLimitsAsync();

        Assert.NotNull(rolloverArgs);

        // Status for CS2 on the new day must now show 0 minutes used!
        var newDayStatus = await service.GetCurrentStatusAsync(_cs2.Id);
        Assert.Equal(0, newDayStatus.MinutesUsedToday);
        Assert.Equal(60, newDayStatus.MinutesRemaining);
    }

    [Fact]
    public async Task GetAllCurrentStatuses_ReturnsStatusForEveryMonitoredGame()
    {
        await using var service = await CreateServiceAsync();

        var statuses = await service.GetAllCurrentStatusesAsync();

        Assert.Equal(2, statuses.Count);
        Assert.Contains(statuses, s => s.GameName == "Counter-Strike 2");
        Assert.Contains(statuses, s => s.GameName == "Valorant");
    }

    [Fact]
    public async Task LimitManagerService_StartAndStop_ManagesLifecycleCorrectly()
    {
        await using var service = await CreateServiceAsync();

        Assert.False(service.IsRunning);

        await service.StartAsync();
        Assert.True(service.IsRunning);

        await service.StopAsync();
        Assert.False(service.IsRunning);
    }
}

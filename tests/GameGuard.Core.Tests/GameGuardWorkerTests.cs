using GameGuard.Core.Tests.Mocks;
using GameGuard.Infrastructure.Security;
using GameGuard.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class GameGuardWorkerTests
{
    private readonly MockDatabaseInitializer _dbInitializer;
    private readonly MockTimeTrackerService _timeTracker;
    private readonly MockProcessMonitor _processMonitor;
    private readonly MockLimitManagerService _limitManager;
    private readonly MockEnforcementService _enforcementService;
    private readonly MockRelaunchSuppressionService _relaunchService;
    private readonly MockAdminOverrideRepository _overrideRepository;
    private readonly MockGameProfileRepository _profileRepository;
    private readonly ClockTamperDetector _tamperDetector;
    private readonly MockIpcServer _ipcServer;
    private readonly MockSystemClock _clock;
    private readonly GameGuardWorker _worker;

    public GameGuardWorkerTests()
    {
        _dbInitializer = new MockDatabaseInitializer();
        _timeTracker = new MockTimeTrackerService();
        _processMonitor = new MockProcessMonitor();
        _limitManager = new MockLimitManagerService();
        _enforcementService = new MockEnforcementService();
        _relaunchService = new MockRelaunchSuppressionService();
        _overrideRepository = new MockAdminOverrideRepository();
        _profileRepository = new MockGameProfileRepository();
        _tamperDetector = new ClockTamperDetector(NullLogger<ClockTamperDetector>.Instance);
        _ipcServer = new MockIpcServer();
        _clock = new MockSystemClock();

        _worker = new GameGuardWorker(
            _dbInitializer,
            _timeTracker,
            _processMonitor,
            _limitManager,
            _enforcementService,
            _relaunchService,
            _overrideRepository,
            _profileRepository,
            _tamperDetector,
            _ipcServer,
            _clock,
            NullLogger<GameGuardWorker>.Instance);
    }

    [Fact]
    public async Task Worker_ExecutesStartup_InitializesDatabaseAndEngines()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Start worker and cancel after a short window
        var workerTask = _worker.StartAsync(cts.Token);
        await Task.Delay(100);

        Assert.True(_dbInitializer.Initialized);
        Assert.True(_timeTracker.IsInitialized);
        Assert.True(_timeTracker.ReconcileCalled);
        Assert.True(_processMonitor.StartCalled);
        Assert.True(_limitManager.StartCalled);
        Assert.True(_enforcementService.StartCalled);
        Assert.True(_relaunchService.StartCalled);
        Assert.True(_ipcServer.StartCalled);

        cts.Cancel();
        await _worker.StopAsync(CancellationToken.None);

        Assert.True(_processMonitor.StopCalled);
        Assert.True(_limitManager.StopCalled);
        Assert.True(_enforcementService.StopCalled);
        Assert.True(_relaunchService.StopCalled);
        Assert.True(_ipcServer.StopCalled);
    }
}

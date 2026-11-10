using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using GameGuard.Core.Services;
using GameGuard.Core.Tests.Mocks;
using GameGuard.Infrastructure.Analytics;
using GameGuard.Infrastructure.Limits;
using GameGuard.Infrastructure.Monitoring;
using GameGuard.Infrastructure.Persistence;
using GameGuard.Infrastructure.Tracking;
using GameGuard.UI.Mvvm;
using GameGuard.UI.Services;
using GameGuard.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class MainViewModelTests : IDisposable
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

    public MainViewModelTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"gameguard_ui_{Guid.NewGuid():N}.db");
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
            NullLogger<TimeTrackerService>.Instance);
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

    private IServiceProvider CreateTestServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, MockDialogService>();
        services.AddSingleton<IAdminAuthService, MockAdminAuthService>();
        services.AddSingleton<IAdminOverrideRepository, MockAdminOverrideRepository>();
        services.AddSingleton<IEnforcementService, MockEnforcementService>();
        services.AddSingleton<ILimitManagerService, MockLimitManagerService>();
        services.AddSingleton<IRelaunchBlockRepository, MockRelaunchBlockRepository>();

        services.AddTransient<DashboardViewModel>(sp => new DashboardViewModel(
            sp.GetRequiredService<ILimitManagerService>(),
            _timeTracker,
            _gameRepo,
            sp.GetRequiredService<IAdminOverrideRepository>(),
            sp.GetRequiredService<IEnforcementService>(),
            sp.GetRequiredService<IAdminAuthService>(),
            sp.GetRequiredService<IDialogService>()));

        services.AddTransient<GamesViewModel>(sp => new GamesViewModel(
            _gameRepo,
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<IAdminAuthService>()));

        services.AddTransient<LimitsViewModel>(sp => new LimitsViewModel(
            _limitRepo,
            _gameRepo,
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<IAdminAuthService>()));

        var boundaryCalc = new DayBoundaryCalculator();
        services.AddSingleton<IDayBoundaryCalculator>(boundaryCalc);
        services.AddSingleton<ISessionAnalyticsService>(sp => new SessionAnalyticsService(
            _sessionRepo,
            sp.GetRequiredService<IRelaunchBlockRepository>(),
            _gameRepo,
            _limitRepo,
            boundaryCalc,
            _systemClock));

        services.AddTransient<HistoryViewModel>(sp => new HistoryViewModel(
            sp.GetRequiredService<ISessionAnalyticsService>(),
            _gameRepo,
            _sessionRepo,
            sp.GetRequiredService<IRelaunchBlockRepository>(),
            boundaryCalc,
            _systemClock));

        services.AddTransient<SettingsViewModel>(sp => new SettingsViewModel(
            sp.GetRequiredService<IAdminAuthService>(),
            sp.GetRequiredService<IAdminOverrideRepository>(),
            _gameRepo,
            sp.GetRequiredService<IDialogService>()));

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task MainViewModel_InitializesAndNavigatesToDashboard()
    {
        await _initializer.InitializeAsync();

        var provider = CreateTestServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var mainVm = new MainViewModel(nav, _processMonitor, _timeTracker);

        await mainVm.InitializeAsync();

        Assert.NotNull(mainVm.CurrentViewModel);
        Assert.IsType<DashboardViewModel>(mainVm.CurrentViewModel);
        Assert.Equal("GameGuard", mainVm.Title);
    }

    [Fact]
    public async Task MainViewModel_NavigationCommands_SwitchCurrentViewModel()
    {
        await _initializer.InitializeAsync();

        var provider = CreateTestServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var mainVm = new MainViewModel(nav, _processMonitor, _timeTracker);

        mainVm.NavigateGamesCommand.Execute(null);
        Assert.IsType<GamesViewModel>(mainVm.CurrentViewModel);

        mainVm.NavigateLimitsCommand.Execute(null);
        Assert.IsType<LimitsViewModel>(mainVm.CurrentViewModel);

        mainVm.NavigateHistoryCommand.Execute(null);
        Assert.IsType<HistoryViewModel>(mainVm.CurrentViewModel);

        mainVm.NavigateSettingsCommand.Execute(null);
        Assert.IsType<SettingsViewModel>(mainVm.CurrentViewModel);

        mainVm.NavigateDashboardCommand.Execute(null);
        Assert.IsType<DashboardViewModel>(mainVm.CurrentViewModel);
    }

    [Fact]
    public void MainViewModel_UpdatesActiveSessionCountsLive()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INavigationService, NavigationService>();
        var provider = services.BuildServiceProvider();
        var nav = provider.GetRequiredService<INavigationService>();

        var mainVm = new MainViewModel(nav, _processMonitor, _timeTracker);

        Assert.Equal(0, mainVm.ActiveGamesCount);
        Assert.Contains("Idle", mainVm.StatusMessage);

        mainVm.UpdateActiveSessionCounts();
        Assert.Equal(0, mainVm.ActiveGamesCount);
    }

    private sealed class MockRelaunchBlockRepository : IRelaunchBlockRepository
    {
        public Task RecordBlockAsync(RelaunchBlockRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<RelaunchBlockRecord>> GetRecentBlocksAsync(int limit = 50, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RelaunchBlockRecord>>(Array.Empty<RelaunchBlockRecord>());
        public Task<int> GetBlockCountForGameTodayAsync(Guid gameProfileId, DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }
}

using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using GameGuard.Core.Services;
using GameGuard.Core.Tests.Mocks;
using GameGuard.Infrastructure.Monitoring;
using GameGuard.Infrastructure.Persistence;
using GameGuard.Infrastructure.Tracking;
using GameGuard.UI.Mvvm;
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

    [Fact]
    public async Task MainViewModel_InitializesAndNavigatesToDashboard()
    {
        await _initializer.InitializeAsync();

        var services = new ServiceCollection();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddTransient<DashboardViewModel>(sp => new DashboardViewModel(
            new MockLimitManagerService(),
            _timeTracker,
            _gameRepo));
        services.AddTransient<GamesViewModel>(sp => new GamesViewModel(_gameRepo));
        services.AddTransient<LimitsViewModel>(sp => new LimitsViewModel(_limitRepo, _gameRepo));
        services.AddTransient<HistoryViewModel>(sp => new HistoryViewModel(_sessionRepo, new MockRelaunchBlockRepository()));
        services.AddTransient<SettingsViewModel>(sp => new SettingsViewModel(new MockAdminAuthService(), new MockAdminOverrideRepository()));

        var provider = services.BuildServiceProvider();
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

        var services = new ServiceCollection();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddTransient<DashboardViewModel>(sp => new DashboardViewModel(
            new MockLimitManagerService(),
            _timeTracker,
            _gameRepo));
        services.AddTransient<GamesViewModel>(sp => new GamesViewModel(_gameRepo));
        services.AddTransient<LimitsViewModel>(sp => new LimitsViewModel(_limitRepo, _gameRepo));
        services.AddTransient<HistoryViewModel>(sp => new HistoryViewModel(_sessionRepo, new MockRelaunchBlockRepository()));
        services.AddTransient<SettingsViewModel>(sp => new SettingsViewModel(new MockAdminAuthService(), new MockAdminOverrideRepository()));

        var provider = services.BuildServiceProvider();
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

    private sealed class MockLimitManagerService : ILimitManagerService
    {
        public event EventHandler<LimitWarningEventArgs>? LimitWarningIssued { add { } remove { } }
        public event EventHandler<GracePeriodEventArgs>? GracePeriodStarted { add { } remove { } }
        public event EventHandler<GracePeriodEventArgs>? GracePeriodUpdated { add { } remove { } }
        public event EventHandler<QuotaExhaustedEventArgs>? QuotaExhausted { add { } remove { } }
        public event EventHandler<DayRolloverEventArgs>? DayRolloverOccurred { add { } remove { } }

        public bool IsRunning => false;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CheckLimitsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LimitCalculationResult> GetCurrentStatusAsync(Guid gameProfileId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LimitCalculationResult
            {
                GameProfileId = gameProfileId,
                GameName = "MockGame",
                MinutesUsedToday = 15,
                EffectiveLimitMinutes = 60,
                SecondsRemaining = 2700
            });
        }
        public Task<IReadOnlyList<LimitCalculationResult>> GetAllCurrentStatusesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LimitCalculationResult>>(Array.Empty<LimitCalculationResult>());
        }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MockRelaunchBlockRepository : IRelaunchBlockRepository
    {
        public Task RecordBlockAsync(RelaunchBlockRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<RelaunchBlockRecord>> GetRecentBlocksAsync(int limit = 50, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RelaunchBlockRecord>>(Array.Empty<RelaunchBlockRecord>());
        public Task<int> GetBlockCountForGameTodayAsync(Guid gameProfileId, DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class MockAdminAuthService : IAdminAuthService
    {
        public Task<bool> IsPasswordConfiguredAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task SetPasswordAsync(string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ValidatePasswordAsync(string password, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public bool IsLockedOut(out TimeSpan remainingLockout)
        {
            remainingLockout = TimeSpan.Zero;
            return false;
        }
    }

    private sealed class MockAdminOverrideRepository : IAdminOverrideRepository
    {
        public Task<IReadOnlyList<AdminOverride>> GetActiveOverridesAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AdminOverride>>(Array.Empty<AdminOverride>());
        public Task<AdminOverride?> GetActiveOverrideForGameAsync(Guid gameProfileId, DateTime nowUtc, CancellationToken cancellationToken = default)
            => Task.FromResult<AdminOverride?>(null);
        public Task AddOverrideAsync(AdminOverride adminOverride, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeOverrideAsync(Guid overrideId, DateTime revokedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AdminOverride>> GetOverrideHistoryAsync(int limit = 50, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AdminOverride>>(Array.Empty<AdminOverride>());
    }
}

using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using GameGuard.Core.Services;
using GameGuard.Core.Tests.Mocks;
using GameGuard.Infrastructure.Persistence;
using GameGuard.UI.Services;
using GameGuard.UI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class InteractiveUiTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteDatabaseInitializer _initializer;
    private readonly SqliteGameProfileRepository _gameRepo;
    private readonly SqliteLimitRepository _limitRepo;
    private readonly SqliteAdminOverrideRepository _overrideRepo;
    private readonly MockDialogService _dialogService;
    private readonly MockAdminAuthService _authService;
    private readonly MockEnforcementService _enforcementService;
    private readonly MockTimeTrackerService _timeTracker;
    private readonly MockLimitManagerService _limitManager;

    private GameProfile _cs2 = null!;

    public InteractiveUiTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"gameguard_interactive_{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new SqliteDatabaseInitializer(_connectionFactory, NullLogger<SqliteDatabaseInitializer>.Instance);
        _gameRepo = new SqliteGameProfileRepository(_connectionFactory);
        _limitRepo = new SqliteLimitRepository(_connectionFactory);
        _overrideRepo = new SqliteAdminOverrideRepository(_connectionFactory);
        _dialogService = new MockDialogService();
        _authService = new MockAdminAuthService();
        _enforcementService = new MockEnforcementService();
        _timeTracker = new MockTimeTrackerService();
        _limitManager = new MockLimitManagerService();
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

    private async Task InitializeDatabaseAsync()
    {
        await _initializer.InitializeAsync();
        var games = await _gameRepo.GetAllAsync();
        _cs2 = games.First(g => g.Name == "Counter-Strike 2");
    }

    [Fact]
    public async Task GamesViewModel_AddGameCommand_AddsNewProfileToRepository()
    {
        await InitializeDatabaseAsync();
        var vm = new GamesViewModel(_gameRepo, _dialogService, _authService);
        await vm.InitializeAsync();

        _dialogService.GameProfileResult = new GameProfileEditModel
        {
            Name = "Apex Legends",
            ProcessNames = "r5apex.exe, apex.exe",
            ExecutablePath = @"C:\Games\Apex\r5apex.exe",
            IsMonitored = true
        };

        vm.AddGameCommand.Execute(null);

        var games = await _gameRepo.GetAllAsync();
        var apex = Assert.Single(games, g => g.Name == "Apex Legends");
        Assert.Equal(2, apex.ProcessNames.Count);
        Assert.True(apex.MatchesProcess("r5apex.exe"));
        Assert.Contains(vm.Games, g => g.Name == "Apex Legends");
    }

    [Fact]
    public async Task GamesViewModel_EditGameCommand_UpdatesProfileInRepository()
    {
        await InitializeDatabaseAsync();
        var vm = new GamesViewModel(_gameRepo, _dialogService, _authService);
        await vm.InitializeAsync();

        _dialogService.GameProfileResult = new GameProfileEditModel
        {
            Name = "Counter-Strike 2 Global",
            ProcessNames = "cs2.exe, csgo.exe",
            ExecutablePath = @"C:\Steam\cs2.exe",
            IsMonitored = true
        };

        vm.EditGameCommand.Execute(_cs2);

        var updated = await _gameRepo.GetByIdAsync(_cs2.Id);
        Assert.NotNull(updated);
        Assert.Equal("Counter-Strike 2 Global", updated!.Name);
        Assert.Equal(2, updated.ProcessNames.Count);
    }

    [Fact]
    public async Task GamesViewModel_DeleteGameCommand_WhenConfirmed_RemovesFromRepository()
    {
        await InitializeDatabaseAsync();
        var vm = new GamesViewModel(_gameRepo, _dialogService, _authService);
        await vm.InitializeAsync();

        _dialogService.ConfirmResult = true;

        vm.DeleteGameCommand.Execute(_cs2);

        var deleted = await _gameRepo.GetByIdAsync(_cs2.Id);
        Assert.Null(deleted);
        Assert.DoesNotContain(vm.Games, g => g.Id == _cs2.Id);
    }

    [Fact]
    public async Task LimitsViewModel_SaveSharedLimitCommand_SavesSharedPool()
    {
        await InitializeDatabaseAsync();
        var vm = new LimitsViewModel(_limitRepo, _gameRepo, _dialogService, _authService);
        await vm.InitializeAsync();

        vm.SharedLimitMinutes = 90;
        vm.SharedGracePeriodSeconds = 120;
        vm.SharedResetHour = 4;
        vm.IsSharedLimitActive = true;

        vm.SaveSharedLimitCommand.Execute(null);

        var shared = await _limitRepo.GetSharedLimitAsync();
        Assert.NotNull(shared);
        Assert.Equal(90, shared!.DailyLimitMinutes);
        Assert.Equal(120, shared.GracePeriodSeconds);
        Assert.Equal(4, shared.ResetHourUtc);
    }

    [Fact]
    public async Task LimitsViewModel_SaveIndividualLimit_SavesSpecificGameAllowance()
    {
        await InitializeDatabaseAsync();
        var vm = new LimitsViewModel(_limitRepo, _gameRepo, _dialogService, _authService);
        await vm.InitializeAsync();

        var cs2Item = Assert.Single(vm.IndividualLimits, l => l.Game.Id == _cs2.Id);
        cs2Item.DailyLimitMinutes = 45;
        cs2Item.GracePeriodSeconds = 90;
        cs2Item.IsActive = true;

        cs2Item.SaveCommand.Execute(null);

        var limit = await _limitRepo.GetIndividualLimitAsync(_cs2.Id);
        Assert.NotNull(limit);
        Assert.Equal(45, limit!.DailyLimitMinutes);
        Assert.Equal(90, limit.GracePeriodSeconds);
        Assert.True(limit.IsActive);
    }

    [Fact]
    public async Task DashboardViewModel_QuickBonus15mCommand_AppliesBonusTimeOverride()
    {
        await InitializeDatabaseAsync();
        var vm = new DashboardViewModel(
            _limitManager,
            _timeTracker,
            _gameRepo,
            _overrideRepo,
            _enforcementService,
            _authService,
            _dialogService);

        var activeDisplay = new ActiveSessionDisplayModel
        {
            SessionId = Guid.NewGuid(),
            GameProfileId = _cs2.Id,
            GameName = "Counter-Strike 2",
            ProcessId = 8800,
            ProcessName = "cs2.exe"
        };

        vm.QuickBonus15mCommand.Execute(activeDisplay);

        var activeOverrides = await _overrideRepo.GetActiveOverridesAsync(DateTime.UtcNow);
        var bonus = Assert.Single(activeOverrides);
        Assert.Equal(_cs2.Id, bonus.GameProfileId);
        Assert.Equal(OverrideType.BonusTime, bonus.Type);
        Assert.Equal(15, bonus.BonusMinutes);
    }

    [Fact]
    public async Task DashboardViewModel_EndSessionNowCommand_InvokesEnforcementService()
    {
        await InitializeDatabaseAsync();
        var vm = new DashboardViewModel(
            _limitManager,
            _timeTracker,
            _gameRepo,
            _overrideRepo,
            _enforcementService,
            _authService,
            _dialogService);

        _dialogService.ConfirmResult = true;

        var activeDisplay = new ActiveSessionDisplayModel
        {
            SessionId = Guid.NewGuid(),
            GameProfileId = _cs2.Id,
            GameName = "Counter-Strike 2",
            ProcessId = 8801,
            ProcessName = "cs2.exe"
        };

        vm.EndSessionNowCommand.Execute(activeDisplay);

        var enforcedCall = Assert.Single(_enforcementService.EnforcedCalls);
        Assert.Equal(8801, enforcedCall.ProcessId);
        Assert.Equal("Counter-Strike 2", enforcedCall.Game.Name);
    }

    [Fact]
    public async Task DashboardViewModel_EmergencyLockCommand_AppliesLockOverrideAndTerminates()
    {
        await InitializeDatabaseAsync();
        var vm = new DashboardViewModel(
            _limitManager,
            _timeTracker,
            _gameRepo,
            _overrideRepo,
            _enforcementService,
            _authService,
            _dialogService);

        _dialogService.ConfirmResult = true;
        _timeTracker.SetActiveSessions(new[]
        {
            new GamingSession
            {
                Id = Guid.NewGuid(),
                GameProfileId = _cs2.Id,
                ProcessId = 8802,
                ProcessName = "cs2.exe",
                StartTimeUtc = DateTime.UtcNow
            }
        });

        var result = new LimitCalculationResult
        {
            GameProfileId = _cs2.Id,
            GameName = "Counter-Strike 2",
            MinutesUsedToday = 30,
            EffectiveLimitMinutes = 60
        };

        vm.EmergencyLockCommand.Execute(result);

        var overrides = await _overrideRepo.GetActiveOverridesAsync(DateTime.UtcNow);
        var lockOverride = Assert.Single(overrides);
        Assert.Equal(OverrideType.EmergencyLock, lockOverride.Type);
        Assert.Equal(_cs2.Id, lockOverride.GameProfileId);

        var enforced = Assert.Single(_enforcementService.EnforcedCalls);
        Assert.Equal(8802, enforced.ProcessId);
    }

    [Fact]
    public async Task SettingsViewModel_SetOrChangePasswordCommand_ConfiguresPassword()
    {
        await InitializeDatabaseAsync();
        var vm = new SettingsViewModel(_authService, _overrideRepo, _gameRepo, _dialogService);

        _dialogService.PasswordToReturn = "StrongP@ssw0rd!";

        vm.SetOrChangePasswordCommand.Execute(null);

        Assert.True(await _authService.ValidatePasswordAsync("StrongP@ssw0rd!"));
    }

    [Fact]
    public async Task SettingsViewModel_CreateAndRevokeOverride_UpdatesRepository()
    {
        await InitializeDatabaseAsync();
        var vm = new SettingsViewModel(_authService, _overrideRepo, _gameRepo, _dialogService);
        await vm.InitializeAsync();

        _dialogService.AddOverrideResult = new AdminOverrideCreateModel
        {
            GameProfileId = _cs2.Id,
            Type = OverrideType.TemporaryUnlock,
            DurationMinutes = 60,
            Reason = "Homework complete reward"
        };

        vm.CreateOverrideCommand.Execute(null);

        var overrides = await _overrideRepo.GetActiveOverridesAsync(DateTime.UtcNow);
        var unlock = Assert.Single(overrides);
        Assert.Equal(OverrideType.TemporaryUnlock, unlock.Type);

        // Now revoke it
        _dialogService.ConfirmResult = true;
        vm.RevokeOverrideCommand.Execute(unlock);

        var remaining = await _overrideRepo.GetActiveOverridesAsync(DateTime.UtcNow);
        Assert.Empty(remaining);
    }

    private sealed class MockTimeTrackerService : ITimeTrackerService
    {
        private readonly List<GamingSession> _sessions = new();

        public event EventHandler<SessionLifecycleEventArgs>? SessionStarted { add { } remove { } }
        public event EventHandler<SessionLifecycleEventArgs>? SessionUpdated { add { } remove { } }
        public event EventHandler<SessionLifecycleEventArgs>? SessionEnded { add { } remove { } }

        public void SetActiveSessions(IEnumerable<GamingSession> sessions)
        {
            _sessions.Clear();
            _sessions.AddRange(sessions);
        }

        public IReadOnlyList<GamingSession> GetActiveSessions() => _sessions.ToList();
        public GamingSession? GetActiveSession(int processId) => _sessions.FirstOrDefault(s => s.ProcessId == processId);
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReconcileOrphanedSessionsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

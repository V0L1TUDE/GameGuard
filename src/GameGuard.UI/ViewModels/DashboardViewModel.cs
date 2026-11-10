using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Ipc;
using GameGuard.Core.Models;
using GameGuard.UI.Mvvm;
using GameGuard.UI.Services;

namespace GameGuard.UI.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly ILimitManagerService _limitManager;
    private readonly ITimeTrackerService _timeTracker;
    private readonly IGameProfileRepository _gameRepository;
    private readonly IAdminOverrideRepository _overrideRepository;
    private readonly IEnforcementService _enforcementService;
    private readonly IAdminAuthService _authService;
    private readonly IDialogService _dialogService;
    private readonly IIpcClient? _ipcClient;

    private readonly DispatcherTimer? _liveTicker;
    private int _monitoredGamesCount;
    private int _activeSessionsCount;
    private int _totalMinutesUsedToday;
    private bool _hasExhaustedGames;

    public int MonitoredGamesCount
    {
        get => _monitoredGamesCount;
        set => SetProperty(ref _monitoredGamesCount, value);
    }

    public int ActiveSessionsCount
    {
        get => _activeSessionsCount;
        set => SetProperty(ref _activeSessionsCount, value);
    }

    public int TotalMinutesUsedToday
    {
        get => _totalMinutesUsedToday;
        set => SetProperty(ref _totalMinutesUsedToday, value);
    }

    public bool HasExhaustedGames
    {
        get => _hasExhaustedGames;
        set => SetProperty(ref _hasExhaustedGames, value);
    }

    public ObservableCollection<LimitCalculationResult> GameStatuses { get; } = new();
    public ObservableCollection<ActiveSessionDisplayModel> ActiveSessionsList { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand QuickBonus15mCommand { get; }
    public ICommand EndSessionNowCommand { get; }
    public ICommand EmergencyLockCommand { get; }

    public DashboardViewModel(
        ILimitManagerService limitManager,
        ITimeTrackerService timeTracker,
        IGameProfileRepository gameRepository,
        IAdminOverrideRepository overrideRepository,
        IEnforcementService enforcementService,
        IAdminAuthService authService,
        IDialogService dialogService,
        IIpcClient? ipcClient = null)
    {
        _limitManager = limitManager;
        _timeTracker = timeTracker;
        _gameRepository = gameRepository;
        _overrideRepository = overrideRepository;
        _enforcementService = enforcementService;
        _authService = authService;
        _dialogService = dialogService;
        _ipcClient = ipcClient;

        Title = "Dashboard";

        RefreshCommand = new AsyncRelayCommand(LoadDashboardDataAsync);
        QuickBonus15mCommand = new AsyncRelayCommand(param => OnQuickBonus15mAsync(param as ActiveSessionDisplayModel));
        EndSessionNowCommand = new AsyncRelayCommand(param => OnEndSessionNowAsync(param as ActiveSessionDisplayModel));
        EmergencyLockCommand = new AsyncRelayCommand(param => OnEmergencyLockAsync(param as LimitCalculationResult));

        _timeTracker.SessionStarted += OnSessionActivity;
        _timeTracker.SessionUpdated += OnSessionActivity;
        _timeTracker.SessionEnded += OnSessionActivity;

        if (System.Windows.Application.Current != null)
        {
            _liveTicker = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _liveTicker.Tick += (s, e) => TickActiveSessions();
            _liveTicker.Start();
        }
    }

    public override async Task InitializeAsync()
    {
        await LoadDashboardDataAsync();
    }

    private void OnSessionActivity(object? sender, SessionLifecycleEventArgs e)
    {
        _ = LoadDashboardDataAsync();
    }

    private void TickActiveSessions()
    {
        foreach (var session in ActiveSessionsList)
        {
            session.DurationSeconds++;
        }
    }

    public async Task LoadDashboardDataAsync()
    {
        IsBusy = true;
        try
        {
            var games = await _gameRepository.GetMonitoredAsync();
            MonitoredGamesCount = games.Count;

            var active = _timeTracker.GetActiveSessions();
            ActiveSessionsCount = active.Count;

            var statuses = await _limitManager.GetAllCurrentStatusesAsync();
            GameStatuses.Clear();
            int totalUsed = 0;
            bool anyExhausted = false;

            foreach (var status in statuses)
            {
                GameStatuses.Add(status);
                totalUsed += status.MinutesUsedToday;
                if (status.IsLimitExceeded)
                {
                    anyExhausted = true;
                }
            }

            TotalMinutesUsedToday = totalUsed;
            HasExhaustedGames = anyExhausted;

            // Update live active sessions list
            ActiveSessionsList.Clear();
            foreach (var act in active)
            {
                var game = games.FirstOrDefault(g => g.Id == act.GameProfileId);
                var status = statuses.FirstOrDefault(s => s.GameProfileId == act.GameProfileId);

                string statusText = "In Session";
                if (status != null)
                {
                    if (status.IsInGracePeriod)
                    {
                        statusText = $"Grace Countdown: {status.GracePeriodSecondsRemaining}s";
                    }
                    else if (status.IsLimitExceeded)
                    {
                        statusText = "Allowance Exhausted";
                    }
                    else if (status.MinutesRemaining.HasValue)
                    {
                        statusText = $"{status.MinutesRemaining.Value}m Remaining";
                    }
                }

                ActiveSessionsList.Add(new ActiveSessionDisplayModel
                {
                    SessionId = act.Id,
                    GameProfileId = act.GameProfileId,
                    GameName = game?.Name ?? act.ProcessName,
                    ProcessId = act.ProcessId,
                    ProcessName = act.ProcessName,
                    DurationSeconds = act.DurationSeconds,
                    MinutesRemaining = status?.MinutesRemaining,
                    StatusText = statusText
                });
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> AuthorizeAdminActionAsync()
    {
        if (await _authService.IsPasswordConfiguredAsync())
        {
            if (_authService.IsLockedOut(out var remaining))
            {
                await _dialogService.ShowMessageAsync(
                    "Action Locked Out",
                    $"Administrator security lockout in effect ({Math.Ceiling(remaining.TotalMinutes)} minutes remaining).",
                    isError: true);
                return false;
            }

            var password = await _dialogService.PromptPasswordAsync(
                "Admin Authorization",
                "Please enter your administrator password to authorize this action.");

            if (string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            bool valid = await _authService.ValidatePasswordAsync(password);
            if (!valid)
            {
                await _dialogService.ShowMessageAsync("Access Denied", "Incorrect administrator password.", isError: true);
                return false;
            }
        }

        return true;
    }

    private async Task OnQuickBonus15mAsync(ActiveSessionDisplayModel? session)
    {
        if (session == null)
        {
            return;
        }

        if (!await AuthorizeAdminActionAsync())
        {
            return;
        }

        if (_ipcClient != null && _ipcClient.IsConnected)
        {
            await _ipcClient.SendRequestAsync<GrantBonusRequest, CommandResultResponse>(
                IpcActions.GrantBonus,
                new GrantBonusRequest(session.GameProfileId, 15, $"Quick +15m bonus granted for {session.GameName}"));
        }
        else
        {
            var overrideRecord = AdminOverride.CreateBonusTime(
                session.GameProfileId,
                15,
                DateTime.UtcNow,
                TimeSpan.FromHours(2),
                $"Quick +15m bonus granted for {session.GameName}");

            await _overrideRepository.AddOverrideAsync(overrideRecord);
        }

        await LoadDashboardDataAsync();
        await _dialogService.ShowMessageAsync("Bonus Playtime Granted", $"+15 minutes bonus granted to '{session.GameName}'.");
    }

    private async Task OnEndSessionNowAsync(ActiveSessionDisplayModel? session)
    {
        if (session == null)
        {
            return;
        }

        if (!await AuthorizeAdminActionAsync())
        {
            return;
        }

        bool confirmed = await _dialogService.ConfirmAsync(
            "Terminate Session",
            $"Are you sure you want to terminate {session.GameName} (PID: {session.ProcessId}) immediately?");

        if (!confirmed)
        {
            return;
        }

        if (_ipcClient != null && _ipcClient.IsConnected)
        {
            await _ipcClient.SendRequestAsync<EndSessionRequest, CommandResultResponse>(
                IpcActions.EndSession,
                new EndSessionRequest(session.ProcessId, "Session terminated manually by admin via UI"));
        }
        else
        {
            var game = await _gameRepository.GetByIdAsync(session.GameProfileId);
            if (game != null)
            {
                await _enforcementService.EnforceGameAsync(game, session.ProcessId, session.ProcessName);
            }
        }

        await LoadDashboardDataAsync();
    }

    private async Task OnEmergencyLockAsync(LimitCalculationResult? result)
    {
        if (result == null)
        {
            return;
        }

        if (!await AuthorizeAdminActionAsync())
        {
            return;
        }

        bool confirmed = await _dialogService.ConfirmAsync(
            "Emergency Lockdown",
            $"Lock down '{result.GameName}' immediately for the next 2 hours?");

        if (!confirmed)
        {
            return;
        }

        if (_ipcClient != null && _ipcClient.IsConnected)
        {
            await _ipcClient.SendRequestAsync<EmergencyLockRequest, CommandResultResponse>(
                IpcActions.EmergencyLock,
                new EmergencyLockRequest(result.GameProfileId, $"Emergency lockdown applied to {result.GameName}"));
        }
        else
        {
            var lockOverride = AdminOverride.CreateEmergencyLock(
                result.GameProfileId,
                DateTime.UtcNow,
                TimeSpan.FromHours(2),
                $"Emergency lockdown applied to {result.GameName}");

            await _overrideRepository.AddOverrideAsync(lockOverride);

            // Terminate any active sessions for this game
            var activeSessions = _timeTracker.GetActiveSessions().Where(s => s.GameProfileId == result.GameProfileId).ToList();
            var game = await _gameRepository.GetByIdAsync(result.GameProfileId);
            if (game != null)
            {
                foreach (var act in activeSessions)
                {
                    await _enforcementService.EnforceGameAsync(game, act.ProcessId, act.ProcessName);
                }
            }
        }

        await LoadDashboardDataAsync();
        await _dialogService.ShowMessageAsync("Lockdown Enforced", $"'{result.GameName}' has been locked down.");
    }

    public override void Dispose()
    {
        _timeTracker.SessionStarted -= OnSessionActivity;
        _timeTracker.SessionUpdated -= OnSessionActivity;
        _timeTracker.SessionEnded -= OnSessionActivity;
        _liveTicker?.Stop();
        base.Dispose();
    }
}

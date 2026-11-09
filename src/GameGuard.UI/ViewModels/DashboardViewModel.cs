using System.Collections.ObjectModel;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using GameGuard.UI.Mvvm;

namespace GameGuard.UI.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly ILimitManagerService _limitManager;
    private readonly ITimeTrackerService _timeTracker;
    private readonly IGameProfileRepository _gameRepository;

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

    public RelayCommand RefreshCommand { get; }

    public DashboardViewModel(
        ILimitManagerService limitManager,
        ITimeTrackerService timeTracker,
        IGameProfileRepository gameRepository)
    {
        _limitManager = limitManager;
        _timeTracker = timeTracker;
        _gameRepository = gameRepository;

        Title = "Dashboard";
        RefreshCommand = new RelayCommand(async () => await LoadDashboardDataAsync());
    }

    public override async Task InitializeAsync()
    {
        await LoadDashboardDataAsync();
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
        }
        finally
        {
            IsBusy = false;
        }
    }
}

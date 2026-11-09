using System.Windows.Input;
using GameGuard.Core.Interfaces;
using GameGuard.UI.Mvvm;

namespace GameGuard.UI.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly IProcessMonitor _processMonitor;
    private readonly ITimeTrackerService _timeTracker;

    private ViewModelBase? _currentViewModel;
    private int _activeGamesCount;
    private bool _isMonitoringActive;
    private string _statusMessage = "GameGuard Online - Monitoring Engines Active";

    public ViewModelBase? CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public int ActiveGamesCount
    {
        get => _activeGamesCount;
        set => SetProperty(ref _activeGamesCount, value);
    }

    public bool IsMonitoringActive
    {
        get => _isMonitoringActive;
        set => SetProperty(ref _isMonitoringActive, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ICommand NavigateDashboardCommand { get; }
    public ICommand NavigateGamesCommand { get; }
    public ICommand NavigateLimitsCommand { get; }
    public ICommand NavigateHistoryCommand { get; }
    public ICommand NavigateSettingsCommand { get; }

    public MainViewModel(
        INavigationService navigationService,
        IProcessMonitor processMonitor,
        ITimeTrackerService timeTracker)
    {
        _navigationService = navigationService;
        _processMonitor = processMonitor;
        _timeTracker = timeTracker;

        Title = "GameGuard";

        NavigateDashboardCommand = new RelayCommand(() => _navigationService.NavigateTo<DashboardViewModel>());
        NavigateGamesCommand = new RelayCommand(() => _navigationService.NavigateTo<GamesViewModel>());
        NavigateLimitsCommand = new RelayCommand(() => _navigationService.NavigateTo<LimitsViewModel>());
        NavigateHistoryCommand = new RelayCommand(() => _navigationService.NavigateTo<HistoryViewModel>());
        NavigateSettingsCommand = new RelayCommand(() => _navigationService.NavigateTo<SettingsViewModel>());

        _navigationService.CurrentViewModelChanged += (s, e) =>
        {
            CurrentViewModel = _navigationService.CurrentViewModel;
        };

        _timeTracker.SessionStarted += (s, e) => UpdateActiveSessionCounts();
        _timeTracker.SessionEnded += (s, e) => UpdateActiveSessionCounts();

        UpdateActiveSessionCounts();
        IsMonitoringActive = _processMonitor.IsRunning;
    }

    public override Task InitializeAsync()
    {
        _navigationService.NavigateTo<DashboardViewModel>();
        UpdateActiveSessionCounts();
        return Task.CompletedTask;
    }

    public void UpdateActiveSessionCounts()
    {
        ActiveGamesCount = _timeTracker.GetActiveSessions().Count;
        IsMonitoringActive = _processMonitor.IsRunning;
        StatusMessage = ActiveGamesCount > 0
            ? $"Monitoring Active • {ActiveGamesCount} Game(s) In Session"
            : "Protected • All Monitored Games Idle";
    }
}

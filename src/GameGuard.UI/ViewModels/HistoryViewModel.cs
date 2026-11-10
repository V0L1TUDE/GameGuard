using System.Collections.ObjectModel;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using GameGuard.UI.Mvvm;

namespace GameGuard.UI.ViewModels;

public sealed class HistoryViewModel : ViewModelBase
{
    private readonly ISessionAnalyticsService _analyticsService;
    private readonly IGameProfileRepository _gameProfileRepository;
    private readonly ISessionRepository _sessionRepository;
    private readonly IRelaunchBlockRepository _blockRepository;
    private readonly IDayBoundaryCalculator _dayBoundaryCalculator;
    private readonly ISystemClock _clock;

    private Dictionary<Guid, string> _profileMap = new();
    private List<GameProfile> _allProfiles = new();

    private string _totalTodayFormatted = "0m";
    private string _total7DaysFormatted = "0m";
    private string _total30DaysFormatted = "0m";
    private int _totalSessionsCount;
    private int _totalBlockedCount;

    private string _selectedGameFilter = "All Games";
    private string _selectedDateRange = "All Time";

    public string TotalTodayFormatted
    {
        get => _totalTodayFormatted;
        set => SetProperty(ref _totalTodayFormatted, value);
    }

    public string Total7DaysFormatted
    {
        get => _total7DaysFormatted;
        set => SetProperty(ref _total7DaysFormatted, value);
    }

    public string Total30DaysFormatted
    {
        get => _total30DaysFormatted;
        set => SetProperty(ref _total30DaysFormatted, value);
    }

    public int TotalSessionsCount
    {
        get => _totalSessionsCount;
        set => SetProperty(ref _totalSessionsCount, value);
    }

    public int TotalBlockedCount
    {
        get => _totalBlockedCount;
        set => SetProperty(ref _totalBlockedCount, value);
    }

    public ObservableCollection<string> FilterGames { get; } = new();
    public ObservableCollection<string> FilterDateRanges { get; } = new();

    public string SelectedGameFilter
    {
        get => _selectedGameFilter;
        set
        {
            if (SetProperty(ref _selectedGameFilter, value))
            {
                _ = ApplyFiltersAsync();
            }
        }
    }

    public string SelectedDateRange
    {
        get => _selectedDateRange;
        set
        {
            if (SetProperty(ref _selectedDateRange, value))
            {
                _ = ApplyFiltersAsync();
            }
        }
    }

    public ObservableCollection<GameBreakdownDisplayModel> GameBreakdowns { get; } = new();
    public ObservableCollection<GamingSessionDisplayModel> Sessions { get; } = new();
    public ObservableCollection<RelaunchBlockRecord> BlockedRelaunches { get; } = new();

    public RelayCommand RefreshCommand { get; }

    public HistoryViewModel(
        ISessionAnalyticsService analyticsService,
        IGameProfileRepository gameProfileRepository,
        ISessionRepository sessionRepository,
        IRelaunchBlockRepository blockRepository,
        IDayBoundaryCalculator dayBoundaryCalculator,
        ISystemClock clock)
    {
        _analyticsService = analyticsService ?? throw new ArgumentNullException(nameof(analyticsService));
        _gameProfileRepository = gameProfileRepository ?? throw new ArgumentNullException(nameof(gameProfileRepository));
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _blockRepository = blockRepository ?? throw new ArgumentNullException(nameof(blockRepository));
        _dayBoundaryCalculator = dayBoundaryCalculator ?? throw new ArgumentNullException(nameof(dayBoundaryCalculator));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        Title = "Session History & Analytics";

        RefreshCommand = new RelayCommand(async () => await LoadAllDataAsync());

        FilterDateRanges.Add("All Time");
        FilterDateRanges.Add("Today");
        FilterDateRanges.Add("Past 7 Days");
        FilterDateRanges.Add("Past 30 Days");
    }

    public override async Task InitializeAsync()
    {
        await LoadAllDataAsync();
    }

    public async Task LoadAllDataAsync()
    {
        IsBusy = true;
        try
        {
            // 1. Fetch game profiles
            var profiles = await _gameProfileRepository.GetAllAsync().ConfigureAwait(true);
            _allProfiles = profiles.ToList();
            _profileMap = _allProfiles.ToDictionary(p => p.Id, p => p.Name);

            // Populate FilterGames dropdown if not yet populated
            if (FilterGames.Count == 0)
            {
                FilterGames.Add("All Games");
                foreach (var p in _allProfiles)
                {
                    FilterGames.Add(p.Name);
                }
            }

            // 2. Load analytics summary
            var summary = await _analyticsService.GetAnalyticsSummaryAsync().ConfigureAwait(true);
            TotalTodayFormatted = FormatTimeSpan(summary.TotalPlaytimeToday);
            Total7DaysFormatted = FormatTimeSpan(summary.TotalPlaytimePast7Days);
            Total30DaysFormatted = FormatTimeSpan(summary.TotalPlaytimePast30Days);
            TotalSessionsCount = summary.TotalSessionsCount;
            TotalBlockedCount = summary.TotalBlockedRelaunchesCount;

            GameBreakdowns.Clear();
            foreach (var b in summary.GameBreakdowns)
            {
                GameBreakdowns.Add(new GameBreakdownDisplayModel
                {
                    GameProfileId = b.GameProfileId,
                    GameName = b.GameName,
                    TotalPlaytimeFormatted = FormatTimeSpan(b.TotalPlaytime),
                    SessionCount = b.SessionCount,
                    Percentage = b.PercentageOfTotal
                });
            }

            // 3. Load filtered session logs
            await ApplyFiltersAsync().ConfigureAwait(true);

            // 4. Load recent blocked relaunches
            var blocks = await _blockRepository.GetRecentBlocksAsync(50).ConfigureAwait(true);
            BlockedRelaunches.Clear();
            foreach (var block in blocks)
            {
                BlockedRelaunches.Add(block);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyFiltersAsync()
    {
        Guid? profileFilterId = null;
        if (SelectedGameFilter != "All Games")
        {
            var match = _allProfiles.FirstOrDefault(p => p.Name.Equals(SelectedGameFilter, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                profileFilterId = match.Id;
            }
        }

        DateTime? fromUtc = null;
        DateTime? toUtc = null;
        var now = _clock.UtcNow;

        if (SelectedDateRange == "Today")
        {
            var boundary = _dayBoundaryCalculator.GetDayBoundary(now, TimeZoneInfo.Local);
            fromUtc = boundary.StartUtc;
            toUtc = boundary.EndUtc;
        }
        else if (SelectedDateRange == "Past 7 Days")
        {
            fromUtc = now.AddDays(-7);
            toUtc = now.AddDays(1);
        }
        else if (SelectedDateRange == "Past 30 Days")
        {
            fromUtc = now.AddDays(-30);
            toUtc = now.AddDays(1);
        }

        var sessions = await _analyticsService.GetFilteredSessionsAsync(
            profileFilterId,
            fromUtc,
            toUtc,
            limit: 100).ConfigureAwait(true);

        Sessions.Clear();
        foreach (var session in sessions)
        {
            string gameName = _profileMap.TryGetValue(session.GameProfileId, out var name)
                ? name
                : session.ProcessName;

            Sessions.Add(GamingSessionDisplayModel.FromSession(session, gameName));
        }
    }

    private static string FormatTimeSpan(TimeSpan span)
    {
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        }
        return $"{Math.Max(0, (int)span.TotalMinutes)}m";
    }
}

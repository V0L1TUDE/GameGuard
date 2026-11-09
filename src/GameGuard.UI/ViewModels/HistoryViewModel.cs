using System.Collections.ObjectModel;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using GameGuard.UI.Mvvm;

namespace GameGuard.UI.ViewModels;

public sealed class HistoryViewModel : ViewModelBase
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IRelaunchBlockRepository _blockRepository;

    public ObservableCollection<GamingSession> Sessions { get; } = new();
    public ObservableCollection<RelaunchBlockRecord> BlockedRelaunches { get; } = new();

    public RelayCommand RefreshCommand { get; }

    public HistoryViewModel(ISessionRepository sessionRepository, IRelaunchBlockRepository blockRepository)
    {
        _sessionRepository = sessionRepository;
        _blockRepository = blockRepository;
        Title = "Session History & Audit";
        RefreshCommand = new RelayCommand(async () => await LoadHistoryAsync());
    }

    public override async Task InitializeAsync()
    {
        await LoadHistoryAsync();
    }

    public async Task LoadHistoryAsync()
    {
        IsBusy = true;
        try
        {
            var sessions = await _sessionRepository.GetRecentSessionsAsync(50);
            Sessions.Clear();
            foreach (var session in sessions)
            {
                Sessions.Add(session);
            }

            var blocks = await _blockRepository.GetRecentBlocksAsync(50);
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
}

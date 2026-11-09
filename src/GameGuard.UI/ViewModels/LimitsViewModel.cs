using System.Collections.ObjectModel;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using GameGuard.UI.Mvvm;

namespace GameGuard.UI.ViewModels;

public sealed class LimitsViewModel : ViewModelBase
{
    private readonly ILimitRepository _limitRepository;
    private readonly IGameProfileRepository _gameRepository;

    public ObservableCollection<DailyLimit> Limits { get; } = new();

    public RelayCommand RefreshCommand { get; }

    public LimitsViewModel(ILimitRepository limitRepository, IGameProfileRepository gameRepository)
    {
        _limitRepository = limitRepository;
        _gameRepository = gameRepository;
        Title = "Daily Gaming Limits";
        RefreshCommand = new RelayCommand(async () => await LoadLimitsAsync());
    }

    public override async Task InitializeAsync()
    {
        await LoadLimitsAsync();
    }

    public async Task LoadLimitsAsync()
    {
        IsBusy = true;
        try
        {
            var list = await _limitRepository.GetAllLimitsAsync();
            Limits.Clear();
            foreach (var limit in list)
            {
                Limits.Add(limit);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}

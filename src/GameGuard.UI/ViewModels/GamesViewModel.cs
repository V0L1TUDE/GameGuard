using System.Collections.ObjectModel;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using GameGuard.UI.Mvvm;

namespace GameGuard.UI.ViewModels;

public sealed class GamesViewModel : ViewModelBase
{
    private readonly IGameProfileRepository _gameRepository;

    public ObservableCollection<GameProfile> Games { get; } = new();

    public RelayCommand RefreshCommand { get; }

    public GamesViewModel(IGameProfileRepository gameRepository)
    {
        _gameRepository = gameRepository;
        Title = "Monitored Games";
        RefreshCommand = new RelayCommand(async () => await LoadGamesAsync());
    }

    public override async Task InitializeAsync()
    {
        await LoadGamesAsync();
    }

    public async Task LoadGamesAsync()
    {
        IsBusy = true;
        try
        {
            var list = await _gameRepository.GetAllAsync();
            Games.Clear();
            foreach (var game in list)
            {
                Games.Add(game);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}

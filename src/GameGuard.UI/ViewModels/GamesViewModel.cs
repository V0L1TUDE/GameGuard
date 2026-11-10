using System.Collections.ObjectModel;
using System.Windows.Input;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using GameGuard.UI.Mvvm;
using GameGuard.UI.Services;

namespace GameGuard.UI.ViewModels;

public sealed class GamesViewModel : ViewModelBase
{
    private readonly IGameProfileRepository _gameRepository;
    private readonly IDialogService _dialogService;
    private readonly IAdminAuthService _authService;

    public ObservableCollection<GameProfile> Games { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand AddGameCommand { get; }
    public ICommand EditGameCommand { get; }
    public ICommand DeleteGameCommand { get; }
    public ICommand ToggleMonitoredCommand { get; }

    public GamesViewModel(
        IGameProfileRepository gameRepository,
        IDialogService dialogService,
        IAdminAuthService authService)
    {
        _gameRepository = gameRepository;
        _dialogService = dialogService;
        _authService = authService;

        Title = "Monitored Games";

        RefreshCommand = new AsyncRelayCommand(LoadGamesAsync);
        AddGameCommand = new AsyncRelayCommand(OnAddGameAsync);
        EditGameCommand = new AsyncRelayCommand(param => OnEditGameAsync(param as GameProfile));
        DeleteGameCommand = new AsyncRelayCommand(param => OnDeleteGameAsync(param as GameProfile));
        ToggleMonitoredCommand = new AsyncRelayCommand(param => OnToggleMonitoredAsync(param as GameProfile));
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
                "Admin Authorization Required",
                "Please enter your administrator password to modify game profiles.");

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

    private async Task OnAddGameAsync()
    {
        if (!await AuthorizeAdminActionAsync())
        {
            return;
        }

        var model = await _dialogService.PromptGameProfileDialogAsync();
        if (model == null)
        {
            return;
        }

        var processNames = model.ProcessNames
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .ToList();

        var profile = new GameProfile
        {
            Id = Guid.NewGuid(),
            Name = model.Name,
            ExecutablePath = string.IsNullOrWhiteSpace(model.ExecutablePath) ? null : model.ExecutablePath,
            ProcessNames = processNames,
            IsMonitored = model.IsMonitored,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _gameRepository.AddAsync(profile);
        await LoadGamesAsync();
        await _dialogService.ShowMessageAsync("Success", $"Game profile '{profile.Name}' added successfully.");
    }

    private async Task OnEditGameAsync(GameProfile? profile)
    {
        if (profile == null)
        {
            return;
        }

        if (!await AuthorizeAdminActionAsync())
        {
            return;
        }

        var model = await _dialogService.PromptGameProfileDialogAsync(profile);
        if (model == null)
        {
            return;
        }

        profile.Name = model.Name;
        profile.ExecutablePath = string.IsNullOrWhiteSpace(model.ExecutablePath) ? null : model.ExecutablePath;
        profile.ProcessNames = model.ProcessNames
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .ToList();
        profile.IsMonitored = model.IsMonitored;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        await _gameRepository.UpdateAsync(profile);
        await LoadGamesAsync();
        await _dialogService.ShowMessageAsync("Success", $"Game profile '{profile.Name}' updated.");
    }

    private async Task OnDeleteGameAsync(GameProfile? profile)
    {
        if (profile == null)
        {
            return;
        }

        if (!await AuthorizeAdminActionAsync())
        {
            return;
        }

        bool confirmed = await _dialogService.ConfirmAsync(
            "Confirm Delete",
            $"Are you sure you want to delete profile '{profile.Name}'? All associated history will be preserved.");

        if (!confirmed)
        {
            return;
        }

        await _gameRepository.DeleteAsync(profile.Id);
        await LoadGamesAsync();
    }

    private async Task OnToggleMonitoredAsync(GameProfile? profile)
    {
        if (profile == null)
        {
            return;
        }

        if (!await AuthorizeAdminActionAsync())
        {
            return;
        }

        profile.IsMonitored = !profile.IsMonitored;
        profile.UpdatedAtUtc = DateTime.UtcNow;
        await _gameRepository.UpdateAsync(profile);
        await LoadGamesAsync();
    }
}

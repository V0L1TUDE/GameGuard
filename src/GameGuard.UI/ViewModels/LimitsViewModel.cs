using System.Collections.ObjectModel;
using System.Windows.Input;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using GameGuard.UI.Mvvm;
using GameGuard.UI.Services;

namespace GameGuard.UI.ViewModels;

public sealed class LimitsViewModel : ViewModelBase
{
    private readonly ILimitRepository _limitRepository;
    private readonly IGameProfileRepository _gameRepository;
    private readonly IDialogService _dialogService;
    private readonly IAdminAuthService _authService;

    private int _sharedLimitMinutes = 120;
    private int _sharedGracePeriodSeconds = 60;
    private int _sharedResetHour = 0;
    private bool _isSharedLimitActive = true;
    private Guid? _sharedLimitId;

    public int SharedLimitMinutes
    {
        get => _sharedLimitMinutes;
        set => SetProperty(ref _sharedLimitMinutes, value);
    }

    public int SharedGracePeriodSeconds
    {
        get => _sharedGracePeriodSeconds;
        set => SetProperty(ref _sharedGracePeriodSeconds, value);
    }

    public int SharedResetHour
    {
        get => _sharedResetHour;
        set => SetProperty(ref _sharedResetHour, value);
    }

    public bool IsSharedLimitActive
    {
        get => _isSharedLimitActive;
        set => SetProperty(ref _isSharedLimitActive, value);
    }

    public ObservableCollection<GameLimitItemViewModel> IndividualLimits { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand SaveSharedLimitCommand { get; }

    public LimitsViewModel(
        ILimitRepository limitRepository,
        IGameProfileRepository gameRepository,
        IDialogService dialogService,
        IAdminAuthService authService)
    {
        _limitRepository = limitRepository;
        _gameRepository = gameRepository;
        _dialogService = dialogService;
        _authService = authService;

        Title = "Daily Gaming Limits";

        RefreshCommand = new AsyncRelayCommand(LoadLimitsAsync);
        SaveSharedLimitCommand = new AsyncRelayCommand(OnSaveSharedLimitAsync);
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
            var shared = await _limitRepository.GetSharedLimitAsync();
            if (shared != null)
            {
                _sharedLimitId = shared.Id;
                SharedLimitMinutes = shared.DailyLimitMinutes;
                SharedGracePeriodSeconds = shared.GracePeriodSeconds;
                SharedResetHour = shared.ResetHourUtc;
                IsSharedLimitActive = shared.IsActive;
            }

            var games = await _gameRepository.GetAllAsync();
            IndividualLimits.Clear();
            foreach (var game in games)
            {
                var indLimit = await _limitRepository.GetIndividualLimitAsync(game.Id);
                var itemVm = new GameLimitItemViewModel(game, indLimit, OnSaveIndividualLimitAsync);
                IndividualLimits.Add(itemVm);
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
                "Please enter your administrator password to update gaming limits.");

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

    private async Task OnSaveSharedLimitAsync()
    {
        if (!await AuthorizeAdminActionAsync())
        {
            return;
        }

        var shared = new DailyLimit
        {
            Id = _sharedLimitId ?? Guid.NewGuid(),
            GameProfileId = null,
            DailyLimitMinutes = SharedLimitMinutes,
            GracePeriodSeconds = SharedGracePeriodSeconds,
            ResetHourUtc = SharedResetHour,
            IsShared = true,
            IsActive = IsSharedLimitActive,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _limitRepository.UpsertLimitAsync(shared);
        _sharedLimitId = shared.Id;
        await _dialogService.ShowMessageAsync("Success", "Shared daily gaming pool limit saved successfully.");
    }

    private async Task OnSaveIndividualLimitAsync(GameLimitItemViewModel item)
    {
        if (!await AuthorizeAdminActionAsync())
        {
            return;
        }

        var existing = await _limitRepository.GetIndividualLimitAsync(item.Game.Id);
        var limit = new DailyLimit
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            GameProfileId = item.Game.Id,
            DailyLimitMinutes = item.DailyLimitMinutes,
            GracePeriodSeconds = item.GracePeriodSeconds,
            ResetHourUtc = existing?.ResetHourUtc ?? SharedResetHour,
            IsShared = false,
            IsActive = item.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _limitRepository.UpsertLimitAsync(limit);
        await _dialogService.ShowMessageAsync("Success", $"Limit for '{item.Game.Name}' saved successfully ({item.DailyLimitMinutes} minutes).");
    }
}

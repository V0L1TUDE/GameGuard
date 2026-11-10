using System.Collections.ObjectModel;
using System.Windows.Input;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using GameGuard.UI.Mvvm;
using GameGuard.UI.Services;

namespace GameGuard.UI.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IAdminAuthService _authService;
    private readonly IAdminOverrideRepository _overrideRepository;
    private readonly IGameProfileRepository _gameRepository;
    private readonly IDialogService _dialogService;

    private bool _isPasswordConfigured;
    private bool _isLockedOut;
    private string _statusMessage = string.Empty;

    public bool IsPasswordConfigured
    {
        get => _isPasswordConfigured;
        set => SetProperty(ref _isPasswordConfigured, value);
    }

    public bool IsLockedOut
    {
        get => _isLockedOut;
        set => SetProperty(ref _isLockedOut, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ObservableCollection<AdminOverride> ActiveOverrides { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand SetOrChangePasswordCommand { get; }
    public ICommand CreateOverrideCommand { get; }
    public ICommand RevokeOverrideCommand { get; }

    public SettingsViewModel(
        IAdminAuthService authService,
        IAdminOverrideRepository overrideRepository,
        IGameProfileRepository gameRepository,
        IDialogService dialogService)
    {
        _authService = authService;
        _overrideRepository = overrideRepository;
        _gameRepository = gameRepository;
        _dialogService = dialogService;

        Title = "Settings & Administration";

        RefreshCommand = new AsyncRelayCommand(LoadSettingsAsync);
        SetOrChangePasswordCommand = new AsyncRelayCommand(OnSetOrChangePasswordAsync);
        CreateOverrideCommand = new AsyncRelayCommand(OnCreateOverrideAsync);
        RevokeOverrideCommand = new AsyncRelayCommand(param => OnRevokeOverrideAsync(param as AdminOverride));
    }

    public override async Task InitializeAsync()
    {
        await LoadSettingsAsync();
    }

    public async Task LoadSettingsAsync()
    {
        IsBusy = true;
        try
        {
            IsPasswordConfigured = await _authService.IsPasswordConfiguredAsync();
            IsLockedOut = _authService.IsLockedOut(out var remaining);
            StatusMessage = IsLockedOut
                ? $"Admin authentication locked out ({Math.Ceiling(remaining.TotalMinutes)} min remaining)."
                : IsPasswordConfigured
                    ? "Admin password is active and secured with PBKDF2 HMAC-SHA256."
                    : "No admin password configured. Set one below to prevent unauthorized configuration changes.";

            var overrides = await _overrideRepository.GetActiveOverridesAsync(DateTime.UtcNow);
            ActiveOverrides.Clear();
            foreach (var item in overrides)
            {
                ActiveOverrides.Add(item);
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

    private async Task OnSetOrChangePasswordAsync()
    {
        if (!await AuthorizeAdminActionAsync())
        {
            return;
        }

        var newPassword = await _dialogService.PromptPasswordAsync(
            "Configure Administrator Password",
            "Enter the new administrator password / PIN:");

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return;
        }

        var confirmPassword = await _dialogService.PromptPasswordAsync(
            "Confirm Administrator Password",
            "Re-enter the new administrator password to confirm:");

        if (newPassword != confirmPassword)
        {
            await _dialogService.ShowMessageAsync("Mismatch", "Passwords do not match.", isError: true);
            return;
        }

        await _authService.SetPasswordAsync(newPassword);
        await LoadSettingsAsync();
        await _dialogService.ShowMessageAsync("Success", "Administrator password has been successfully configured.");
    }

    private async Task OnCreateOverrideAsync()
    {
        if (!await AuthorizeAdminActionAsync())
        {
            return;
        }

        var games = await _gameRepository.GetAllAsync();
        var model = await _dialogService.PromptAddOverrideDialogAsync(games);
        if (model == null)
        {
            return;
        }

        AdminOverride overrideRecord;
        var now = DateTime.UtcNow;

        if (model.Type == OverrideType.BonusTime)
        {
            overrideRecord = AdminOverride.CreateBonusTime(
                model.GameProfileId,
                model.BonusMinutes,
                now,
                TimeSpan.FromMinutes(model.DurationMinutes),
                model.Reason);
        }
        else if (model.Type == OverrideType.TemporaryUnlock)
        {
            overrideRecord = AdminOverride.CreateTemporaryUnlock(
                model.GameProfileId,
                now,
                TimeSpan.FromMinutes(model.DurationMinutes),
                model.Reason);
        }
        else
        {
            overrideRecord = AdminOverride.CreateEmergencyLock(
                model.GameProfileId,
                now,
                TimeSpan.FromMinutes(model.DurationMinutes),
                model.Reason);
        }

        await _overrideRepository.AddOverrideAsync(overrideRecord);
        await LoadSettingsAsync();
        await _dialogService.ShowMessageAsync("Success", "Administrator override has been applied.");
    }

    private async Task OnRevokeOverrideAsync(AdminOverride? item)
    {
        if (item == null)
        {
            return;
        }

        if (!await AuthorizeAdminActionAsync())
        {
            return;
        }

        bool confirmed = await _dialogService.ConfirmAsync(
            "Revoke Override",
            $"Are you sure you want to revoke this {item.Type} override immediately?");

        if (!confirmed)
        {
            return;
        }

        await _overrideRepository.RevokeOverrideAsync(item.Id, DateTime.UtcNow);
        await LoadSettingsAsync();
    }
}

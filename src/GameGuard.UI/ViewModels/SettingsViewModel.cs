using System.Collections.ObjectModel;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using GameGuard.UI.Mvvm;

namespace GameGuard.UI.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IAdminAuthService _authService;
    private readonly IAdminOverrideRepository _overrideRepository;

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

    public RelayCommand RefreshCommand { get; }

    public SettingsViewModel(IAdminAuthService authService, IAdminOverrideRepository overrideRepository)
    {
        _authService = authService;
        _overrideRepository = overrideRepository;
        Title = "Settings & Administration";
        RefreshCommand = new RelayCommand(async () => await LoadSettingsAsync());
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
                ? $"Admin auth locked out ({Math.Ceiling(remaining.TotalMinutes)} min remaining)."
                : IsPasswordConfigured
                    ? "Admin password is protected with PBKDF2 HMAC-SHA256."
                    : "No admin password configured. Set one to prevent tampering.";

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
}

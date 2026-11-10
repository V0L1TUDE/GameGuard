using System.Windows.Input;
using GameGuard.Core.Models;
using GameGuard.UI.Mvvm;

namespace GameGuard.UI.ViewModels;

public sealed class GameLimitItemViewModel : ObservableObject
{
    private int _dailyLimitMinutes = 60;
    private int _gracePeriodSeconds = 60;
    private bool _isActive = true;

    public GameProfile Game { get; }

    public int DailyLimitMinutes
    {
        get => _dailyLimitMinutes;
        set => SetProperty(ref _dailyLimitMinutes, value);
    }

    public int GracePeriodSeconds
    {
        get => _gracePeriodSeconds;
        set => SetProperty(ref _gracePeriodSeconds, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public ICommand SaveCommand { get; }

    public GameLimitItemViewModel(GameProfile game, DailyLimit? limit, Func<GameLimitItemViewModel, Task> onSave)
    {
        Game = game;
        if (limit != null)
        {
            DailyLimitMinutes = limit.DailyLimitMinutes;
            GracePeriodSeconds = limit.GracePeriodSeconds;
            IsActive = limit.IsActive;
        }

        SaveCommand = new AsyncRelayCommand(() => onSave(this));
    }
}

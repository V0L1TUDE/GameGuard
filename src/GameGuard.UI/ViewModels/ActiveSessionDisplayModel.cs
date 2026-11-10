using GameGuard.UI.Mvvm;

namespace GameGuard.UI.ViewModels;

public sealed class ActiveSessionDisplayModel : ObservableObject
{
    private int _durationSeconds;
    private int? _minutesRemaining;
    private string _statusText = "In Session";

    public Guid SessionId { get; init; }
    public Guid GameProfileId { get; init; }
    public string GameName { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;

    public int DurationSeconds
    {
        get => _durationSeconds;
        set
        {
            if (SetProperty(ref _durationSeconds, value))
            {
                OnPropertyChanged(nameof(ElapsedFormatted));
            }
        }
    }

    public string ElapsedFormatted
    {
        get
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, DurationSeconds));
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
    }

    public int? MinutesRemaining
    {
        get => _minutesRemaining;
        set => SetProperty(ref _minutesRemaining, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }
}

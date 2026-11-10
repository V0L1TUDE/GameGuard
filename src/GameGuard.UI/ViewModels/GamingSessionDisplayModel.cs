using System.Windows.Media;
using GameGuard.Core.Models;

namespace GameGuard.UI.ViewModels;

public sealed class GamingSessionDisplayModel
{
    public Guid Id { get; init; }
    public Guid GameProfileId { get; init; }
    public string GameName { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public string StartTimeLocalFormatted { get; init; } = string.Empty;
    public string EndTimeLocalFormatted { get; init; } = string.Empty;
    public string DurationFormatted { get; init; } = string.Empty;
    public string TerminationBadgeText { get; init; } = string.Empty;
    public Brush TerminationBadgeBackground { get; init; } = Brushes.SlateGray;
    public string? Notes { get; init; }

    public static GamingSessionDisplayModel FromSession(GamingSession session, string gameName)
    {
        var localStart = session.StartTimeUtc.ToLocalTime();
        var localEnd = session.EndTimeUtc?.ToLocalTime();

        string durationStr = FormatDuration(session.DurationSeconds);

        var (badgeText, badgeColor) = session.TerminationReason switch
        {
            SessionTerminationReason.UserClosed => ("Normal Exit", Color.FromRgb(0x10, 0xB9, 0x81)),
            SessionTerminationReason.LimitEnforced => ("Limit Enforced", Color.FromRgb(0xEF, 0x44, 0x44)),
            SessionTerminationReason.ForcedByAdmin => ("Admin Closed", Color.FromRgb(0xF5, 0x9E, 0x0B)),
            SessionTerminationReason.CrashOrExternal => ("Crash / External", Color.FromRgb(0x64, 0x74, 0x8B)),
            _ => (session.EndTimeUtc == null ? "Playing Now" : "Ended", Color.FromRgb(0x00, 0xE6, 0x76))
        };

        return new GamingSessionDisplayModel
        {
            Id = session.Id,
            GameProfileId = session.GameProfileId,
            GameName = gameName,
            ProcessName = session.ProcessName,
            ProcessId = session.ProcessId,
            StartTimeLocalFormatted = localStart.ToString("yyyy-MM-dd HH:mm:ss"),
            EndTimeLocalFormatted = localEnd.HasValue ? localEnd.Value.ToString("yyyy-MM-dd HH:mm:ss") : "Active",
            DurationFormatted = durationStr,
            TerminationBadgeText = badgeText,
            TerminationBadgeBackground = new SolidColorBrush(badgeColor),
            Notes = session.Notes
        };
    }

    private static string FormatDuration(int totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
        }
        if (ts.TotalMinutes >= 1)
        {
            return $"{ts.Minutes}m {ts.Seconds}s";
        }
        return $"{ts.Seconds}s";
    }
}

public sealed class GameBreakdownDisplayModel
{
    public Guid GameProfileId { get; init; }
    public string GameName { get; init; } = string.Empty;
    public string TotalPlaytimeFormatted { get; init; } = string.Empty;
    public int SessionCount { get; init; }
    public double Percentage { get; init; }
    public string PercentageFormatted => $"{Percentage:F1}%";
    public double BarWidthPercentage => Math.Max(4, Math.Min(100, Percentage));
}

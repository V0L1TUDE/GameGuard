namespace GameGuard.Core.Models;

public sealed record SessionAnalyticsSummary(
    TimeSpan TotalPlaytimeToday,
    TimeSpan TotalPlaytimePast7Days,
    TimeSpan TotalPlaytimePast30Days,
    int TotalSessionsCount,
    int TotalBlockedRelaunchesCount,
    IReadOnlyList<GamePlaytimeBreakdown> GameBreakdowns,
    IReadOnlyDictionary<SessionTerminationReason, int> TerminationBreakdowns
);

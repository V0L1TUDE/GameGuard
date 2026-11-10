namespace GameGuard.Core.Models;

public sealed record GamePlaytimeBreakdown(
    Guid GameProfileId,
    string GameName,
    TimeSpan TotalPlaytime,
    int SessionCount,
    double PercentageOfTotal
);

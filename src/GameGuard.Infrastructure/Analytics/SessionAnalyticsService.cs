using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Extensions.Logging;

namespace GameGuard.Infrastructure.Analytics;

public sealed class SessionAnalyticsService : ISessionAnalyticsService
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IRelaunchBlockRepository _blockRepository;
    private readonly IGameProfileRepository _gameProfileRepository;
    private readonly ILimitRepository _limitRepository;
    private readonly IDayBoundaryCalculator _dayBoundaryCalculator;
    private readonly ISystemClock _clock;
    private readonly ILogger<SessionAnalyticsService>? _logger;

    private readonly TimeZoneInfo _timeZone;

    public SessionAnalyticsService(
        ISessionRepository sessionRepository,
        IRelaunchBlockRepository blockRepository,
        IGameProfileRepository gameProfileRepository,
        ILimitRepository limitRepository,
        IDayBoundaryCalculator dayBoundaryCalculator,
        ISystemClock clock,
        TimeZoneInfo? timeZone = null,
        ILogger<SessionAnalyticsService>? logger = null)
    {
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _blockRepository = blockRepository ?? throw new ArgumentNullException(nameof(blockRepository));
        _gameProfileRepository = gameProfileRepository ?? throw new ArgumentNullException(nameof(gameProfileRepository));
        _limitRepository = limitRepository ?? throw new ArgumentNullException(nameof(limitRepository));
        _dayBoundaryCalculator = dayBoundaryCalculator ?? throw new ArgumentNullException(nameof(dayBoundaryCalculator));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _logger = logger;
    }

    public async Task<SessionAnalyticsSummary> GetAnalyticsSummaryAsync(
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        var now = asOfUtc ?? _clock.UtcNow;

        var sharedLimit = await _limitRepository.GetSharedLimitAsync(cancellationToken).ConfigureAwait(false);
        int resetHour = sharedLimit?.ResetHourUtc ?? 0;

        var todayBoundary = _dayBoundaryCalculator.GetDayBoundary(now, _timeZone, resetHour);
        var sevenDaysAgoUtc = todayBoundary.StartUtc.AddDays(-6);
        var thirtyDaysAgoUtc = todayBoundary.StartUtc.AddDays(-29);

        // Fetch all sessions in the 30-day window
        var sessions = await _sessionRepository.GetSessionsForDateRangeAsync(
            thirtyDaysAgoUtc,
            now.AddDays(1),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // Fetch registered profiles for game name resolution
        var profiles = await _gameProfileRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var profileMap = profiles.ToDictionary(p => p.Id, p => p.Name);

        // Calculate playtime metrics
        var todaySessions = sessions.Where(s => s.StartTimeUtc >= todayBoundary.StartUtc && s.StartTimeUtc < todayBoundary.EndUtc).ToList();
        var sevenDaySessions = sessions.Where(s => s.StartTimeUtc >= sevenDaysAgoUtc).ToList();

        long todaySeconds = todaySessions.Sum(s => (long)s.DurationSeconds);
        long sevenDaySeconds = sevenDaySessions.Sum(s => (long)s.DurationSeconds);
        long thirtyDaySeconds = sessions.Sum(s => (long)s.DurationSeconds);

        // Per-game breakdown across the 30-day window
        var gameGroups = sessions
            .GroupBy(s => s.GameProfileId)
            .Select(g =>
            {
                var gameId = g.Key;
                string gameName = profileMap.TryGetValue(gameId, out var name)
                    ? name
                    : (g.FirstOrDefault()?.ProcessName ?? "Unknown Game");

                long durationSec = g.Sum(s => (long)s.DurationSeconds);
                int count = g.Count();
                double percent = thirtyDaySeconds > 0
                    ? Math.Round((double)durationSec / thirtyDaySeconds * 100.0, 1)
                    : 0.0;

                return new GamePlaytimeBreakdown(
                    gameId,
                    gameName,
                    TimeSpan.FromSeconds(durationSec),
                    count,
                    percent);
            })
            .OrderByDescending(b => b.TotalPlaytime)
            .ToList();

        // Termination reasons breakdown
        var terminationCounts = sessions
            .GroupBy(s => s.TerminationReason)
            .ToDictionary(g => g.Key, g => g.Count());

        // Blocked relaunch count in 30-day window
        var recentBlocks = await _blockRepository.GetRecentBlocksAsync(500, cancellationToken).ConfigureAwait(false);
        int blockedCount = recentBlocks.Count(b => b.BlockedAtUtc >= thirtyDaysAgoUtc);

        return new SessionAnalyticsSummary(
            TotalPlaytimeToday: TimeSpan.FromSeconds(todaySeconds),
            TotalPlaytimePast7Days: TimeSpan.FromSeconds(sevenDaySeconds),
            TotalPlaytimePast30Days: TimeSpan.FromSeconds(thirtyDaySeconds),
            TotalSessionsCount: sessions.Count,
            TotalBlockedRelaunchesCount: blockedCount,
            GameBreakdowns: gameGroups,
            TerminationBreakdowns: terminationCounts
        );
    }

    public async Task<IReadOnlyList<GamingSession>> GetFilteredSessionsAsync(
        Guid? gameProfileId = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (fromUtc.HasValue || toUtc.HasValue || gameProfileId.HasValue)
        {
            var start = fromUtc ?? DateTime.MinValue;
            var end = toUtc ?? DateTime.MaxValue;

            var results = await _sessionRepository.GetSessionsForDateRangeAsync(
                start,
                end,
                gameProfileId,
                cancellationToken).ConfigureAwait(false);

            return results.Take(limit).ToList();
        }

        return await _sessionRepository.GetRecentSessionsAsync(limit, cancellationToken).ConfigureAwait(false);
    }
}

using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface ISessionAnalyticsService
{
    Task<SessionAnalyticsSummary> GetAnalyticsSummaryAsync(
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GamingSession>> GetFilteredSessionsAsync(
        Guid? gameProfileId = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        int limit = 100,
        CancellationToken cancellationToken = default);
}

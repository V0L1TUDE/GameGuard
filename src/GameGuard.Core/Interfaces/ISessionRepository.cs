using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface ISessionRepository
{
    Task<GamingSession?> GetActiveSessionByProcessIdAsync(int processId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GamingSession>> GetActiveSessionsAsync(CancellationToken cancellationToken = default);
    Task StartSessionAsync(GamingSession session, CancellationToken cancellationToken = default);
    Task UpdateSessionHeartbeatAsync(Guid sessionId, int durationSeconds, CancellationToken cancellationToken = default);
    Task EndSessionAsync(Guid sessionId, DateTime endTimeUtc, SessionTerminationReason reason, string? notes = null, CancellationToken cancellationToken = default);
    Task<int> GetTotalSecondsUsedForGameTodayAsync(Guid gameProfileId, DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken cancellationToken = default);
    Task<int> GetTotalSharedSecondsUsedTodayAsync(DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GamingSession>> GetRecentSessionsAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GamingSession>> GetSessionsForDateRangeAsync(DateTime startUtc, DateTime endUtc, Guid? gameProfileId = null, CancellationToken cancellationToken = default);
}

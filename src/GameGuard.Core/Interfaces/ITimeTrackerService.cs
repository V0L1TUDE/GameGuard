using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface ITimeTrackerService : IAsyncDisposable, IDisposable
{
    event EventHandler<SessionLifecycleEventArgs>? SessionStarted;
    event EventHandler<SessionLifecycleEventArgs>? SessionUpdated;
    event EventHandler<SessionLifecycleEventArgs>? SessionEnded;

    IReadOnlyList<GamingSession> GetActiveSessions();
    GamingSession? GetActiveSession(int processId);

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ReconcileOrphanedSessionsAsync(CancellationToken cancellationToken = default);
}

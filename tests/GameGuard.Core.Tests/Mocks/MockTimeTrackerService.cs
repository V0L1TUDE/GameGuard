using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockTimeTrackerService : ITimeTrackerService
{
    public event EventHandler<SessionLifecycleEventArgs>? SessionStarted { add { } remove { } }
    public event EventHandler<SessionLifecycleEventArgs>? SessionUpdated { add { } remove { } }
    public event EventHandler<SessionLifecycleEventArgs>? SessionEnded { add { } remove { } }

    public bool IsInitialized { get; private set; }
    public bool ReconcileCalled { get; private set; }
    public List<GamingSession> ActiveSessionsList { get; } = new();

    public IReadOnlyList<GamingSession> GetActiveSessions() => ActiveSessionsList;

    public GamingSession? GetActiveSession(int processId) => ActiveSessionsList.FirstOrDefault(s => s.ProcessId == processId);

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsInitialized = true;
        return Task.CompletedTask;
    }

    public Task ReconcileOrphanedSessionsAsync(CancellationToken cancellationToken = default)
    {
        ReconcileCalled = true;
        return Task.CompletedTask;
    }

    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

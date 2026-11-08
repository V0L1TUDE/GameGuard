namespace GameGuard.Core.Models;

public sealed class SessionLifecycleEventArgs : EventArgs
{
    public required GameProfile Game { get; init; }
    public required GamingSession Session { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public required TimeSpan Duration { get; init; }
    public SessionTerminationReason? TerminationReason { get; init; }
}

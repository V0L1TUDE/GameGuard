namespace GameGuard.Core.Models;

public enum ProcessEventType
{
    Started = 1,
    Running = 2,
    Exited = 3
}

public sealed class ProcessLifecycleEventArgs : EventArgs
{
    public required GameProfile Game { get; init; }
    public required DetectedProcessInfo Process { get; init; }
    public required ProcessEventType EventType { get; init; }
    public required DateTime TimestampUtc { get; init; }
}

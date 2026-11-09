namespace GameGuard.Core.Models;

public sealed class ClockTamperEventArgs : EventArgs
{
    public required DateTime LastRecordedUtc { get; init; }
    public required DateTime CurrentSystemUtc { get; init; }
    public required TimeSpan RollbackDelta { get; init; }
    public required DateTime DetectedAtUtc { get; init; }
}

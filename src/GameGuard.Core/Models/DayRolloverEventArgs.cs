namespace GameGuard.Core.Models;

public sealed class DayRolloverEventArgs : EventArgs
{
    public required DateTime PreviousDayStartUtc { get; init; }
    public required DateTime NewDayStartUtc { get; init; }
    public required DateTime TimestampUtc { get; init; }
}

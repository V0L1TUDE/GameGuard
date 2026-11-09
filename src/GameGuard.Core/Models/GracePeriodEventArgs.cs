namespace GameGuard.Core.Models;

public sealed class GracePeriodEventArgs : EventArgs
{
    public required GameProfile Game { get; init; }
    public required LimitCalculationResult CalculationResult { get; init; }
    public required int GracePeriodSecondsRemaining { get; init; }
    public required DateTime TimestampUtc { get; init; }
}

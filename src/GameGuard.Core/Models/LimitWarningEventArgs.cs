namespace GameGuard.Core.Models;

public sealed class LimitWarningEventArgs : EventArgs
{
    public required GameProfile Game { get; init; }
    public required LimitCalculationResult CalculationResult { get; init; }
    public required int WarningThresholdMinutes { get; init; }
    public required DateTime TimestampUtc { get; init; }
}

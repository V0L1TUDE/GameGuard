namespace GameGuard.Core.Models;

public sealed class QuotaExhaustedEventArgs : EventArgs
{
    public required GameProfile Game { get; init; }
    public required LimitCalculationResult CalculationResult { get; init; }
    public required DateTime TimestampUtc { get; init; }
}

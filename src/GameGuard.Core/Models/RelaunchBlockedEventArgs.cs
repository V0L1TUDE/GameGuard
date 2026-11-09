namespace GameGuard.Core.Models;

public sealed class RelaunchBlockedEventArgs : EventArgs
{
    public required GameProfile Game { get; init; }
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required DateTime BlockedAtUtc { get; init; }
    public required int AttemptCountInWindow { get; init; }
    public TimeSpan? CooldownRemaining { get; init; }
    public required string Reason { get; init; }
}

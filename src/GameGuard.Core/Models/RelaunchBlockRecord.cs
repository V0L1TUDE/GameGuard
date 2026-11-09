namespace GameGuard.Core.Models;

public sealed class RelaunchBlockRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid GameProfileId { get; init; }
    public int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public DateTime BlockedAtUtc { get; init; } = DateTime.UtcNow;
    public required string Reason { get; init; }
}

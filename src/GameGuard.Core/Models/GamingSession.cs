namespace GameGuard.Core.Models;

public sealed class GamingSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid GameProfileId { get; init; }
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public DateTime StartTimeUtc { get; init; } = DateTime.UtcNow;
    public DateTime? EndTimeUtc { get; set; }
    public int DurationSeconds { get; set; }
    public SessionTerminationReason TerminationReason { get; set; } = SessionTerminationReason.Active;
    public string? Notes { get; set; }

    public bool IsActive => EndTimeUtc == null;
}

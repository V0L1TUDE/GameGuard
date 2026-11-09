namespace GameGuard.Core.Models;

public sealed record EnforcementResult
{
    public required Guid GameProfileId { get; init; }
    public required string GameName { get; init; }
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required bool Success { get; init; }
    public required EnforcementMethod MethodUsed { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public string? ErrorMessage { get; init; }
}

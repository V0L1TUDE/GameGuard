namespace GameGuard.Core.Models;

public sealed record DetectedProcessInfo(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    int ParentProcessId);

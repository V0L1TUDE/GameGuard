namespace GameGuard.Core.Ipc;

public static class IpcActions
{
    public const string GetStatus = "GetStatus";
    public const string GrantBonus = "GrantBonus";
    public const string EmergencyLock = "EmergencyLock";
    public const string EndSession = "EndSession";
    public const string NotificationEvent = "NotificationEvent";
    public const string SessionUpdatedEvent = "SessionUpdatedEvent";
}

public sealed record GrantBonusRequest(Guid? GameProfileId, int BonusMinutes, string? Reason);

public sealed record EmergencyLockRequest(Guid? GameProfileId, string? Reason);

public sealed record EndSessionRequest(int ProcessId, string? Reason);

public sealed record CommandResultResponse(bool Success, string? ErrorMessage = null);

public sealed record ActiveSessionInfo(
    Guid SessionId,
    Guid GameProfileId,
    string GameName,
    int ProcessId,
    string ProcessName,
    int DurationSeconds);

public sealed record ServiceStatusResponse(
    bool IsMonitoringActive,
    int ActiveSessionsCount,
    IReadOnlyList<ActiveSessionInfo> ActiveSessions);

public sealed record NotificationEventPayload(
    string Title,
    string Message,
    int NotificationType);

namespace GameGuard.Core.Ipc;

public sealed class IpcMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = string.Empty; // "Request", "Response", "Event"
    public string Action { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public static IpcMessage CreateRequest(string action, string? payloadJson = null) =>
        new() { Type = "Request", Action = action, PayloadJson = payloadJson };

    public static IpcMessage CreateResponse(string requestId, string action, string? payloadJson = null) =>
        new() { Id = requestId, Type = "Response", Action = action, PayloadJson = payloadJson };

    public static IpcMessage CreateEvent(string action, string? payloadJson = null) =>
        new() { Type = "Event", Action = action, PayloadJson = payloadJson };
}

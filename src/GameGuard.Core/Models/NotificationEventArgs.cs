namespace GameGuard.Core.Models;

public enum NotificationType
{
    Information,
    Warning,
    Danger,
    Success
}

public sealed class NotificationEventArgs : EventArgs
{
    public string Title { get; }
    public string Message { get; }
    public NotificationType Type { get; }
    public DateTime TimestampUtc { get; }

    public NotificationEventArgs(string title, string message, NotificationType type = NotificationType.Information)
    {
        Title = title;
        Message = message;
        Type = type;
        TimestampUtc = DateTime.UtcNow;
    }
}

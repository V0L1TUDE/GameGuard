using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;

namespace GameGuard.Infrastructure.Notifications;

public sealed class NotificationService : INotificationService
{
    public event EventHandler<NotificationEventArgs>? NotificationDispatched;

    public void ShowNotification(string title, string message, NotificationType type = NotificationType.Information)
    {
        NotificationDispatched?.Invoke(this, new NotificationEventArgs(title, message, type));
    }
}

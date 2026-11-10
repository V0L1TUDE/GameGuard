using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface INotificationService
{
    event EventHandler<NotificationEventArgs>? NotificationDispatched;

    void ShowNotification(string title, string message, NotificationType type = NotificationType.Information);
}

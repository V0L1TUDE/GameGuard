using System.Windows;
using GameGuard.Core.Models;

namespace GameGuard.UI.Services.Tray;

public interface ISystemTrayManager : IDisposable
{
    void Initialize(Window mainWindow);
    void UpdateStatus(string statusText);
    void ShowBalloon(string title, string message, NotificationType type = NotificationType.Information);
}

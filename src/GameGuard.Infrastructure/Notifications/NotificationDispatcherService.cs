using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Extensions.Logging;

namespace GameGuard.Infrastructure.Notifications;

public sealed class NotificationDispatcherService : IDisposable
{
    private readonly INotificationService _notificationService;
    private readonly ILimitManagerService _limitManager;
    private readonly IEnforcementService _enforcementService;
    private readonly IRelaunchSuppressionService _relaunchService;
    private readonly IClockTamperDetector? _tamperDetector;
    private readonly ILogger<NotificationDispatcherService>? _logger;
    private bool _isDisposed;

    public NotificationDispatcherService(
        INotificationService notificationService,
        ILimitManagerService limitManager,
        IEnforcementService enforcementService,
        IRelaunchSuppressionService relaunchService,
        IClockTamperDetector? tamperDetector = null,
        ILogger<NotificationDispatcherService>? logger = null)
    {
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _limitManager = limitManager ?? throw new ArgumentNullException(nameof(limitManager));
        _enforcementService = enforcementService ?? throw new ArgumentNullException(nameof(enforcementService));
        _relaunchService = relaunchService ?? throw new ArgumentNullException(nameof(relaunchService));
        _tamperDetector = tamperDetector;
        _logger = logger;

        SubscribeEvents();
    }

    private void SubscribeEvents()
    {
        _limitManager.LimitWarningIssued += OnLimitWarningIssued;
        _limitManager.GracePeriodStarted += OnGracePeriodStarted;
        _enforcementService.GameEnforced += OnGameEnforced;
        _relaunchService.RelaunchBlocked += OnRelaunchBlocked;

        if (_tamperDetector != null)
        {
            _tamperDetector.ClockRollbackDetected += OnClockRollbackDetected;
        }
    }

    private void UnsubscribeEvents()
    {
        _limitManager.LimitWarningIssued -= OnLimitWarningIssued;
        _limitManager.GracePeriodStarted -= OnGracePeriodStarted;
        _enforcementService.GameEnforced -= OnGameEnforced;
        _relaunchService.RelaunchBlocked -= OnRelaunchBlocked;

        if (_tamperDetector != null)
        {
            _tamperDetector.ClockRollbackDetected -= OnClockRollbackDetected;
        }
    }

    private void OnLimitWarningIssued(object? sender, LimitWarningEventArgs e)
    {
        var remaining = e.CalculationResult.SecondsRemaining.HasValue
            ? TimeSpan.FromSeconds(e.CalculationResult.SecondsRemaining.Value)
            : TimeSpan.FromMinutes(e.WarningThresholdMinutes);

        string formattedTime = FormatDuration(remaining);
        _notificationService.ShowNotification(
            "GameGuard Limit Warning",
            $"{e.Game.Name}: You have {formattedTime} remaining today.",
            NotificationType.Warning);
    }

    private void OnGracePeriodStarted(object? sender, GracePeriodEventArgs e)
    {
        _notificationService.ShowNotification(
            "GameGuard: Time's Up",
            $"{e.Game.Name}: Daily limit reached. You have {e.GracePeriodSecondsRemaining}s grace period to save and exit.",
            NotificationType.Warning);
    }

    private void OnGameEnforced(object? sender, GameEnforcedEventArgs e)
    {
        if (e.Result.Success)
        {
            _notificationService.ShowNotification(
                "GameGuard Enforcement",
                $"{e.Result.GameName}: Session closed as daily playtime limit expired.",
                NotificationType.Danger);
        }
    }

    private void OnRelaunchBlocked(object? sender, RelaunchBlockedEventArgs e)
    {
        _notificationService.ShowNotification(
            "GameGuard: Launch Blocked",
            $"{e.ProcessName} blocked: {e.Reason}",
            NotificationType.Danger);
    }

    private void OnClockRollbackDetected(object? sender, ClockTamperEventArgs e)
    {
        _notificationService.ShowNotification(
            "GameGuard Security Warning",
            $"System clock rollback detected ({e.RollbackDelta.TotalMinutes:F0}m). Games remain locked.",
            NotificationType.Danger);
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        }
        if (span.TotalMinutes >= 1)
        {
            return $"{span.Minutes}m";
        }
        return $"{Math.Max(0, span.Seconds)}s";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        UnsubscribeEvents();
    }
}

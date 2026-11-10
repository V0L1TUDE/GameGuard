using GameGuard.Core.Models;
using GameGuard.Core.Services;
using GameGuard.Core.Tests.Mocks;
using GameGuard.Infrastructure.Notifications;
using GameGuard.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class NotificationDispatcherTests : IDisposable
{
    private readonly NotificationService _notificationService;
    private readonly MockLimitManagerService _limitManager;
    private readonly MockEnforcementService _enforcementService;
    private readonly MockRelaunchSuppressionService _relaunchService;
    private readonly ClockTamperDetector _tamperDetector;
    private readonly NotificationDispatcherService _dispatcher;

    private readonly List<NotificationEventArgs> _dispatchedNotifications = new();

    public NotificationDispatcherTests()
    {
        _notificationService = new NotificationService();
        _limitManager = new MockLimitManagerService();
        _enforcementService = new MockEnforcementService();
        _relaunchService = new MockRelaunchSuppressionService();
        _tamperDetector = new ClockTamperDetector(NullLogger<ClockTamperDetector>.Instance);

        _dispatcher = new NotificationDispatcherService(
            _notificationService,
            _limitManager,
            _enforcementService,
            _relaunchService,
            _tamperDetector);

        _notificationService.NotificationDispatched += (s, e) => _dispatchedNotifications.Add(e);
    }

    public void Dispose()
    {
        _dispatcher.Dispose();
    }

    [Fact]
    public void LimitWarningIssued_DispatchesWarningNotification()
    {
        var game = new GameProfile
        {
            Id = Guid.NewGuid(),
            Name = "Counter-Strike 2",
            ProcessNames = new[] { "cs2.exe" }
        };

        var calcResult = new LimitCalculationResult
        {
            GameProfileId = game.Id,
            GameName = game.Name,
            MinutesUsedToday = 45,
            EffectiveLimitMinutes = 60,
            SecondsRemaining = 900 // 15m
        };

        _limitManager.RaiseLimitWarning(new LimitWarningEventArgs
        {
            Game = game,
            CalculationResult = calcResult,
            WarningThresholdMinutes = 15,
            TimestampUtc = DateTime.UtcNow
        });

        Assert.Single(_dispatchedNotifications);
        var notif = _dispatchedNotifications[0];
        Assert.Equal("GameGuard Limit Warning", notif.Title);
        Assert.Contains("Counter-Strike 2", notif.Message);
        Assert.Contains("15m", notif.Message);
        Assert.Equal(NotificationType.Warning, notif.Type);
    }

    [Fact]
    public void GracePeriodStarted_DispatchesWarningNotification()
    {
        var game = new GameProfile
        {
            Id = Guid.NewGuid(),
            Name = "Valorant",
            ProcessNames = new[] { "VALORANT-Win64-Shipping.exe" }
        };

        var calcResult = new LimitCalculationResult
        {
            GameProfileId = game.Id,
            GameName = game.Name,
            MinutesUsedToday = 60,
            EffectiveLimitMinutes = 60,
            SecondsRemaining = 0,
            IsInGracePeriod = true,
            GracePeriodSecondsRemaining = 60
        };

        _limitManager.RaiseGracePeriodStarted(new GracePeriodEventArgs
        {
            Game = game,
            CalculationResult = calcResult,
            GracePeriodSecondsRemaining = 60,
            TimestampUtc = DateTime.UtcNow
        });

        Assert.Single(_dispatchedNotifications);
        var notif = _dispatchedNotifications[0];
        Assert.Equal("GameGuard: Time's Up", notif.Title);
        Assert.Contains("Valorant", notif.Message);
        Assert.Contains("60s grace period", notif.Message);
        Assert.Equal(NotificationType.Warning, notif.Type);
    }

    [Fact]
    public void GameEnforced_DispatchesDangerNotification()
    {
        var game = new GameProfile
        {
            Id = Guid.NewGuid(),
            Name = "Counter-Strike 2",
            ProcessNames = new[] { "cs2.exe" }
        };

        var result = new EnforcementResult
        {
            GameProfileId = game.Id,
            GameName = game.Name,
            ProcessId = 1234,
            ProcessName = "cs2.exe",
            Success = true,
            MethodUsed = EnforcementMethod.GracefulClose,
            TimestampUtc = DateTime.UtcNow
        };

        _enforcementService.RaiseGameEnforced(new GameEnforcedEventArgs
        {
            Result = result
        });

        Assert.Single(_dispatchedNotifications);
        var notif = _dispatchedNotifications[0];
        Assert.Equal("GameGuard Enforcement", notif.Title);
        Assert.Contains("Counter-Strike 2", notif.Message);
        Assert.Contains("Session closed", notif.Message);
        Assert.Equal(NotificationType.Danger, notif.Type);
    }

    [Fact]
    public void RelaunchBlocked_DispatchesDangerNotification()
    {
        var game = new GameProfile
        {
            Id = Guid.NewGuid(),
            Name = "Valorant",
            ProcessNames = new[] { "VALORANT-Win64-Shipping.exe" }
        };

        _relaunchService.RaiseRelaunchBlocked(new RelaunchBlockedEventArgs
        {
            Game = game,
            ProcessId = 4321,
            ProcessName = "VALORANT-Win64-Shipping.exe",
            BlockedAtUtc = DateTime.UtcNow,
            AttemptCountInWindow = 1,
            Reason = "Daily limit exhausted"
        });

        Assert.Single(_dispatchedNotifications);
        var notif = _dispatchedNotifications[0];
        Assert.Equal("GameGuard: Launch Blocked", notif.Title);
        Assert.Contains("VALORANT-Win64-Shipping.exe", notif.Message);
        Assert.Contains("Daily limit exhausted", notif.Message);
        Assert.Equal(NotificationType.Danger, notif.Type);
    }

    [Fact]
    public void ClockRollbackDetected_DispatchesSecurityNotification()
    {
        var now = DateTime.UtcNow;
        _tamperDetector.ValidateSystemTime(now);
        // Rollback clock by 10 minutes
        _tamperDetector.ValidateSystemTime(now.AddMinutes(-10));

        Assert.Single(_dispatchedNotifications);
        var notif = _dispatchedNotifications[0];
        Assert.Equal("GameGuard Security Warning", notif.Title);
        Assert.Contains("rollback detected", notif.Message);
        Assert.Equal(NotificationType.Danger, notif.Type);
    }
}

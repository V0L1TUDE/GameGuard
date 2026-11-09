using GameGuard.Core.Models;
using GameGuard.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class ClockTamperDetectorTests
{
    [Fact]
    public void ValidateSystemTime_WithNormalForwardProgress_UpdatesWatermarkWithoutEvent()
    {
        var initial = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
        var detector = new ClockTamperDetector(NullLogger<ClockTamperDetector>.Instance, initial);

        bool eventFired = false;
        detector.ClockRollbackDetected += (s, e) => eventFired = true;

        detector.ValidateSystemTime(initial.AddMinutes(5));

        Assert.False(eventFired);
        Assert.Equal(initial.AddMinutes(5), detector.LastKnownHighestUtc);
    }

    [Fact]
    public void ValidateSystemTime_WithMinorJitterWithinTolerance_DoesNotFireRollback()
    {
        var initial = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
        var detector = new ClockTamperDetector(NullLogger<ClockTamperDetector>.Instance, initial);

        bool eventFired = false;
        detector.ClockRollbackDetected += (s, e) => eventFired = true;

        // 30-second backward jitter (within 1 minute tolerance)
        detector.ValidateSystemTime(initial.AddSeconds(-30));

        Assert.False(eventFired);
        Assert.Equal(initial, detector.LastKnownHighestUtc);
    }

    [Fact]
    public void ValidateSystemTime_WithRollbackBeyondTolerance_FiresRollbackEvent()
    {
        var initial = new DateTime(2026, 11, 9, 15, 0, 0, DateTimeKind.Utc);
        var detector = new ClockTamperDetector(NullLogger<ClockTamperDetector>.Instance, initial);

        ClockTamperEventArgs? receivedEvent = null;
        detector.ClockRollbackDetected += (s, e) => receivedEvent = e;

        // Roll back clock by 3 hours
        var rolledBack = initial.AddHours(-3);
        detector.ValidateSystemTime(rolledBack);

        Assert.NotNull(receivedEvent);
        Assert.Equal(initial, receivedEvent!.LastRecordedUtc);
        Assert.Equal(rolledBack, receivedEvent.CurrentSystemUtc);
        Assert.Equal(TimeSpan.FromHours(3), receivedEvent.RollbackDelta);
    }
}

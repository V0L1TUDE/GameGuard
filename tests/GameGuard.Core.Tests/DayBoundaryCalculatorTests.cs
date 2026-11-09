using GameGuard.Infrastructure.Limits;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class DayBoundaryCalculatorTests
{
    private readonly DayBoundaryCalculator _calculator = new();

    [Fact]
    public void GetDayBoundary_InUtcAtMidnight_CalculatesAccurateDayBounds()
    {
        var nowUtc = new DateTime(2026, 11, 9, 14, 30, 0, DateTimeKind.Utc);
        var boundary = _calculator.GetDayBoundary(nowUtc, TimeZoneInfo.Utc, resetHour: 0);

        Assert.Equal(new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc), boundary.StartUtc);
        Assert.Equal(new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), boundary.EndUtc);
    }

    [Fact]
    public void GetDayBoundary_WhenTimeIsBeforeResetHour_BelongsToPreviousDayCycle()
    {
        // 03:30 AM is before 04:00 AM reset
        var nowUtc = new DateTime(2026, 11, 9, 3, 30, 0, DateTimeKind.Utc);
        var boundary = _calculator.GetDayBoundary(nowUtc, TimeZoneInfo.Utc, resetHour: 4);

        Assert.Equal(new DateTime(2026, 11, 8, 4, 0, 0, DateTimeKind.Utc), boundary.StartUtc);
        Assert.Equal(new DateTime(2026, 11, 9, 4, 0, 0, DateTimeKind.Utc), boundary.EndUtc);
    }

    [Fact]
    public void GetDayBoundary_WhenTimeIsAfterResetHour_BelongsToCurrentDayCycle()
    {
        // 05:00 AM is after 04:00 AM reset
        var nowUtc = new DateTime(2026, 11, 9, 5, 0, 0, DateTimeKind.Utc);
        var boundary = _calculator.GetDayBoundary(nowUtc, TimeZoneInfo.Utc, resetHour: 4);

        Assert.Equal(new DateTime(2026, 11, 9, 4, 0, 0, DateTimeKind.Utc), boundary.StartUtc);
        Assert.Equal(new DateTime(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc), boundary.EndUtc);
    }

    [Fact]
    public void GetDayBoundary_WithNonUtcTimeZone_ConvertsAccurately()
    {
        // Eastern Standard Time (UTC-5)
        var est = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        // 2026-11-09 02:00:00 UTC is 2026-11-08 21:00:00 EST (9 PM previous calendar day locally)
        var nowUtc = new DateTime(2026, 11, 9, 2, 0, 0, DateTimeKind.Utc);
        var boundary = _calculator.GetDayBoundary(nowUtc, est, resetHour: 0);

        // Midnight EST of Nov 8th corresponds to 05:00 UTC Nov 8th
        Assert.Equal(new DateTime(2026, 11, 8, 5, 0, 0, DateTimeKind.Utc), boundary.StartUtc);
        Assert.Equal(new DateTime(2026, 11, 9, 5, 0, 0, DateTimeKind.Utc), boundary.EndUtc);
    }
}

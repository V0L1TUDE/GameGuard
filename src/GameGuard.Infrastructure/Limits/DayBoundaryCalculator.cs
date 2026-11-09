using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;

namespace GameGuard.Infrastructure.Limits;

public sealed class DayBoundaryCalculator : IDayBoundaryCalculator
{
    public DateTimeRange GetDayBoundary(DateTime nowUtc, TimeZoneInfo timeZone, int resetHour = 0)
    {
        int clampedResetHour = Math.Clamp(resetHour, 0, 23);
        DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone);

        DateTime localDayStart = localNow.Date.AddHours(clampedResetHour);
        if (localNow < localDayStart)
        {
            localDayStart = localDayStart.AddDays(-1);
        }

        DateTime localDayEnd = localDayStart.AddDays(1);

        DateTime dayStartUtc = ConvertLocalToUtc(localDayStart, timeZone);
        DateTime dayEndUtc = ConvertLocalToUtc(localDayEnd, timeZone);

        return new DateTimeRange(dayStartUtc, dayEndUtc);
    }

    private static DateTime ConvertLocalToUtc(DateTime localDateTime, TimeZoneInfo timeZone)
    {
        if (timeZone.IsInvalidTime(localDateTime))
        {
            // Gap during spring-forward: advance to first valid time
            var gap = timeZone.GetUtcOffset(localDateTime.AddHours(2));
            return DateTime.SpecifyKind(localDateTime.Add(gap), DateTimeKind.Utc);
        }

        if (timeZone.IsAmbiguousTime(localDateTime))
        {
            // Overlap during fall-back: pick standard offset
            var offsets = timeZone.GetAmbiguousTimeOffsets(localDateTime);
            var offset = offsets.Min();
            return DateTime.SpecifyKind(localDateTime - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(localDateTime, timeZone);
    }
}

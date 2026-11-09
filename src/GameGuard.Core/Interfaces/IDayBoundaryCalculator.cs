using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface IDayBoundaryCalculator
{
    DateTimeRange GetDayBoundary(DateTime nowUtc, TimeZoneInfo timeZone, int resetHour = 0);
}

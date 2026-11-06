using GameGuard.Core.Interfaces;

namespace GameGuard.Core.Services;

public sealed class SystemClock : ISystemClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime TodayUtc => DateTime.UtcNow.Date;
}

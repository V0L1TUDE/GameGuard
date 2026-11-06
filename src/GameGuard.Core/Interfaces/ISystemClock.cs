namespace GameGuard.Core.Interfaces;

public interface ISystemClock
{
    DateTime UtcNow { get; }
    DateTime TodayUtc { get; }
}

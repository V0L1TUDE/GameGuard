using GameGuard.Core.Interfaces;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockSystemClock : ISystemClock
{
    public DateTime UtcNow { get; set; } = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
    public DateTime TodayUtc => UtcNow.Date;

    public void Advance(TimeSpan timeSpan)
    {
        UtcNow = UtcNow.Add(timeSpan);
    }
}

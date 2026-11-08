using System.Diagnostics;
using GameGuard.Core.Interfaces;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockMonotonicClock : IMonotonicClock
{
    private long _currentTimestamp = 100_000_000;
    private static readonly long Frequency = Stopwatch.Frequency;

    public long GetTimestamp() => _currentTimestamp;

    public void Advance(TimeSpan timeSpan)
    {
        _currentTimestamp += (long)(timeSpan.TotalSeconds * Frequency);
    }

    public TimeSpan GetElapsedTime(long startingTimestamp)
    {
        return GetElapsedTime(startingTimestamp, _currentTimestamp);
    }

    public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
    {
        long ticks = endingTimestamp - startingTimestamp;
        return TimeSpan.FromSeconds((double)ticks / Frequency);
    }
}

namespace GameGuard.Core.Interfaces;

public interface IMonotonicClock
{
    long GetTimestamp();
    TimeSpan GetElapsedTime(long startingTimestamp);
    TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp);
}

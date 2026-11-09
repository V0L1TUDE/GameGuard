using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface IClockTamperDetector
{
    event EventHandler<ClockTamperEventArgs>? ClockRollbackDetected;

    DateTime LastKnownHighestUtc { get; }

    void ValidateSystemTime(DateTime currentUtc);
    void UpdateLastKnownTime(DateTime timestampUtc);
}

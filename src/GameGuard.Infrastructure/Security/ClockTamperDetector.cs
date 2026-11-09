using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Extensions.Logging;

namespace GameGuard.Infrastructure.Security;

public sealed class ClockTamperDetector : IClockTamperDetector
{
    private static readonly TimeSpan RollbackTolerance = TimeSpan.FromMinutes(1);

    private readonly ILogger<ClockTamperDetector> _logger;
    private readonly object _lock = new();
    private DateTime _lastKnownHighestUtc;

    public event EventHandler<ClockTamperEventArgs>? ClockRollbackDetected;

    public DateTime LastKnownHighestUtc
    {
        get
        {
            lock (_lock)
            {
                return _lastKnownHighestUtc;
            }
        }
    }

    public ClockTamperDetector(ILogger<ClockTamperDetector> logger, DateTime? initialKnownUtc = null)
    {
        _logger = logger;
        _lastKnownHighestUtc = initialKnownUtc ?? DateTime.UtcNow;
    }

    public void UpdateLastKnownTime(DateTime timestampUtc)
    {
        lock (_lock)
        {
            if (timestampUtc > _lastKnownHighestUtc)
            {
                _lastKnownHighestUtc = timestampUtc;
            }
        }
    }

    public void ValidateSystemTime(DateTime currentUtc)
    {
        ClockTamperEventArgs? rollbackArgs = null;

        lock (_lock)
        {
            if (currentUtc < _lastKnownHighestUtc - RollbackTolerance)
            {
                var delta = _lastKnownHighestUtc - currentUtc;
                rollbackArgs = new ClockTamperEventArgs
                {
                    LastRecordedUtc = _lastKnownHighestUtc,
                    CurrentSystemUtc = currentUtc,
                    RollbackDelta = delta,
                    DetectedAtUtc = DateTime.UtcNow
                };

                _logger.LogCritical(
                    "CLOCK TAMPER DETECTED: System clock moved backwards by {Delta}! (Recorded: {Recorded:o}, System: {System:o})",
                    delta,
                    _lastKnownHighestUtc,
                    currentUtc);
            }
            else if (currentUtc > _lastKnownHighestUtc)
            {
                _lastKnownHighestUtc = currentUtc;
            }
        }

        if (rollbackArgs != null)
        {
            ClockRollbackDetected?.Invoke(this, rollbackArgs);
        }
    }
}

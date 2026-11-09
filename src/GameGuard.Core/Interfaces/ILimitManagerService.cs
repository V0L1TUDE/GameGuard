using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface ILimitManagerService : IAsyncDisposable, IDisposable
{
    event EventHandler<LimitWarningEventArgs>? LimitWarningIssued;
    event EventHandler<GracePeriodEventArgs>? GracePeriodStarted;
    event EventHandler<GracePeriodEventArgs>? GracePeriodUpdated;
    event EventHandler<QuotaExhaustedEventArgs>? QuotaExhausted;
    event EventHandler<DayRolloverEventArgs>? DayRolloverOccurred;

    Task<LimitCalculationResult> GetCurrentStatusAsync(Guid gameProfileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LimitCalculationResult>> GetAllCurrentStatusesAsync(CancellationToken cancellationToken = default);
    Task CheckLimitsAsync(CancellationToken cancellationToken = default);

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    bool IsRunning { get; }
}

using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockLimitManagerService : ILimitManagerService
{
    public event EventHandler<LimitWarningEventArgs>? LimitWarningIssued { add { } remove { } }
    public event EventHandler<GracePeriodEventArgs>? GracePeriodStarted { add { } remove { } }
    public event EventHandler<GracePeriodEventArgs>? GracePeriodUpdated { add { } remove { } }
    public event EventHandler<QuotaExhaustedEventArgs>? QuotaExhausted { add { } remove { } }
    public event EventHandler<DayRolloverEventArgs>? DayRolloverOccurred { add { } remove { } }

    public bool IsRunning => false;

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CheckLimitsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<LimitCalculationResult> GetCurrentStatusAsync(Guid gameProfileId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new LimitCalculationResult
        {
            GameProfileId = gameProfileId,
            GameName = "MockGame",
            MinutesUsedToday = 15,
            EffectiveLimitMinutes = 60,
            SecondsRemaining = 2700
        });
    }

    public Task<IReadOnlyList<LimitCalculationResult>> GetAllCurrentStatusesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LimitCalculationResult>>(Array.Empty<LimitCalculationResult>());
    }

    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

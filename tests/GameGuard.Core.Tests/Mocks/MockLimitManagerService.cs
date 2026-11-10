using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockLimitManagerService : ILimitManagerService
{
    public event EventHandler<LimitWarningEventArgs>? LimitWarningIssued;
    public event EventHandler<GracePeriodEventArgs>? GracePeriodStarted;
    public event EventHandler<GracePeriodEventArgs>? GracePeriodUpdated;
    public event EventHandler<QuotaExhaustedEventArgs>? QuotaExhausted;
    public event EventHandler<DayRolloverEventArgs>? DayRolloverOccurred;

    public void RaiseLimitWarning(LimitWarningEventArgs e) => LimitWarningIssued?.Invoke(this, e);
    public void RaiseGracePeriodStarted(GracePeriodEventArgs e) => GracePeriodStarted?.Invoke(this, e);
    public void RaiseGracePeriodUpdated(GracePeriodEventArgs e) => GracePeriodUpdated?.Invoke(this, e);
    public void RaiseQuotaExhausted(QuotaExhaustedEventArgs e) => QuotaExhausted?.Invoke(this, e);
    public void RaiseDayRollover(DayRolloverEventArgs e) => DayRolloverOccurred?.Invoke(this, e);

    public bool IsRunning { get; private set; }
    public bool StartCalled { get; private set; }
    public bool StopCalled { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        IsRunning = true;
        StartCalled = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        IsRunning = false;
        StopCalled = true;
        return Task.CompletedTask;
    }
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

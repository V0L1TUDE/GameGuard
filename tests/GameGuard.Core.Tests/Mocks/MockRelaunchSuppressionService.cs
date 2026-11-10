using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockRelaunchSuppressionService : IRelaunchSuppressionService
{
    public event EventHandler<RelaunchBlockedEventArgs>? RelaunchBlocked;

    public void RaiseRelaunchBlocked(RelaunchBlockedEventArgs e) => RelaunchBlocked?.Invoke(this, e);

    public bool IsRunning => true;

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public bool IsInCooldown(Guid gameProfileId, out TimeSpan remainingCooldown)
    {
        remainingCooldown = TimeSpan.Zero;
        return false;
    }

    public Task<bool> CheckAndSuppressIfExhaustedAsync(
        GameProfile game,
        int processId,
        string processName,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<RelaunchBlockRecord>> GetRecentBlockedRelaunchesAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<RelaunchBlockRecord>>(Array.Empty<RelaunchBlockRecord>());
    }

    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

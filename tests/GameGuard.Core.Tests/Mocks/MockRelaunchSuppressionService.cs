using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockRelaunchSuppressionService : IRelaunchSuppressionService
{
    public event EventHandler<RelaunchBlockedEventArgs>? RelaunchBlocked;

    public void RaiseRelaunchBlocked(RelaunchBlockedEventArgs e) => RelaunchBlocked?.Invoke(this, e);

    public bool IsRunning { get; private set; } = true;
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

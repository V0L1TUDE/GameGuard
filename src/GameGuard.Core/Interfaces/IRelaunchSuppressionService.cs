using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface IRelaunchSuppressionService : IDisposable, IAsyncDisposable
{
    event EventHandler<RelaunchBlockedEventArgs>? RelaunchBlocked;

    bool IsRunning { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    bool IsInCooldown(Guid gameProfileId, out TimeSpan remainingCooldown);

    Task<bool> CheckAndSuppressIfExhaustedAsync(
        GameProfile game,
        int processId,
        string processName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RelaunchBlockRecord>> GetRecentBlockedRelaunchesAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);
}

using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface IEnforcementService : IAsyncDisposable, IDisposable
{
    event EventHandler<GameEnforcedEventArgs>? GameEnforced;

    Task<EnforcementResult> EnforceGameAsync(
        GameProfile game,
        int processId,
        string processName,
        TimeSpan? gracefulTimeout = null,
        CancellationToken cancellationToken = default);

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    bool IsRunning { get; }
}

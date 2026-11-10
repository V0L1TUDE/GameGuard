using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockEnforcementService : IEnforcementService
{
    public event EventHandler<GameEnforcedEventArgs>? GameEnforced { add { } remove { } }

    public bool IsRunning => true;

    public List<(GameProfile Game, int ProcessId, string ProcessName)> EnforcedCalls { get; } = new();

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<EnforcementResult> EnforceGameAsync(
        GameProfile game,
        int processId,
        string processName,
        TimeSpan? gracefulTimeout = null,
        CancellationToken cancellationToken = default)
    {
        EnforcedCalls.Add((game, processId, processName));
        return Task.FromResult(new EnforcementResult
        {
            GameProfileId = game.Id,
            GameName = game.Name,
            ProcessId = processId,
            ProcessName = processName,
            Success = true,
            MethodUsed = EnforcementMethod.GracefulClose,
            TimestampUtc = DateTime.UtcNow
        });
    }

    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

using GameGuard.Core.Ipc;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockIpcServer : IIpcServer
{
    public bool IsRunning { get; private set; }
    public int ConnectedClientCount => 0;
    public bool StartCalled { get; private set; }
    public bool StopCalled { get; private set; }

    public Dictionary<string, Func<IpcMessage, Task<IpcMessage>>> Handlers { get; } = new();
    public List<(string Action, object? Payload)> BroadcastedEvents { get; } = new();

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

    public void RegisterHandler(string action, Func<IpcMessage, Task<IpcMessage>> handler)
    {
        Handlers[action] = handler;
    }

    public Task BroadcastEventAsync(string action, object? payload, CancellationToken cancellationToken = default)
    {
        BroadcastedEvents.Add((action, payload));
        return Task.CompletedTask;
    }

    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

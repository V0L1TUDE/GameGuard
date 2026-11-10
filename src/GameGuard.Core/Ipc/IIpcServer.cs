namespace GameGuard.Core.Ipc;

public interface IIpcServer : IAsyncDisposable, IDisposable
{
    bool IsRunning { get; }
    int ConnectedClientCount { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    void RegisterHandler(string action, Func<IpcMessage, Task<IpcMessage>> handler);
    Task BroadcastEventAsync(string action, object? payload, CancellationToken cancellationToken = default);
}

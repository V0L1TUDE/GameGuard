namespace GameGuard.Core.Ipc;

public interface IIpcClient : IAsyncDisposable, IDisposable
{
    bool IsConnected { get; }

    event EventHandler<IpcMessage>? EventReceived;
    event EventHandler? Connected;
    event EventHandler? Disconnected;

    Task<bool> ConnectAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<TResponse?> SendRequestAsync<TRequest, TResponse>(
        string action,
        TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

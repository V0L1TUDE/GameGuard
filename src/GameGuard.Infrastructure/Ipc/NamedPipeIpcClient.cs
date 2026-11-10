using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using GameGuard.Core.Ipc;
using Microsoft.Extensions.Logging;

namespace GameGuard.Infrastructure.Ipc;

public sealed class NamedPipeIpcClient : IIpcClient
{
    private readonly string _pipeName;
    private readonly ILogger<NamedPipeIpcClient>? _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcMessage>> _pendingRequests = new();

    private NamedPipeClientStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private bool _isDisposed;
    private bool _isConnected;

    public bool IsConnected => _isConnected && _stream != null && _stream.IsConnected;

    public event EventHandler<IpcMessage>? EventReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public NamedPipeIpcClient(string pipeName = NamedPipeIpcServer.DefaultPipeName, ILogger<NamedPipeIpcClient>? logger = null)
    {
        _pipeName = pipeName;
        _logger = logger;
    }

    public async Task<bool> ConnectAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (IsConnected) return true;

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(3);

        try
        {
            _stream?.Dispose();
            _stream = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            using var linkCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkCts.CancelAfter(effectiveTimeout);

            await _stream.ConnectAsync(linkCts.Token).ConfigureAwait(false);

            _isConnected = true;
            _cts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_cts.Token), CancellationToken.None);

            _logger?.LogInformation("Successfully connected to GameGuard IPC pipe '{PipeName}'.", _pipeName);
            Connected?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not connect to GameGuard IPC pipe '{PipeName}'.", _pipeName);
            _isConnected = false;
            _stream?.Dispose();
            _stream = null;
            return false;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected && _stream == null) return;

        _isConnected = false;
        _cts?.Cancel();

        if (_stream != null)
        {
            try
            {
                _stream.Dispose();
            }
            catch
            {
            }
            _stream = null;
        }

        if (_readTask != null)
        {
            try
            {
                await _readTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        // Cancel all pending requests
        foreach (var (id, tcs) in _pendingRequests)
        {
            tcs.TrySetCanceled();
        }
        _pendingRequests.Clear();

        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public async Task<TResponse?> SendRequestAsync<TRequest, TResponse>(
        string action,
        TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            bool connected = await ConnectAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            if (!connected)
            {
                return default;
            }
        }

        string? payloadJson = request != null ? JsonSerializer.Serialize(request) : null;
        var requestMsg = IpcMessage.CreateRequest(action, payloadJson);

        var tcs = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[requestMsg.Id] = tcs;

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);

        try
        {
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_stream == null || !_stream.IsConnected)
                {
                    throw new IOException("IPC connection lost before sending request.");
                }
                await IpcMessageFramer.WriteMessageAsync(_stream, requestMsg, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }

            using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, linkedCts.Token)).ConfigureAwait(false);
            if (completedTask == tcs.Task)
            {
                var responseMsg = await tcs.Task.ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(responseMsg.PayloadJson))
                {
                    return default;
                }
                return JsonSerializer.Deserialize<TResponse>(responseMsg.PayloadJson);
            }

            throw new TimeoutException($"IPC request '{action}' timed out after {effectiveTimeout.TotalSeconds:F1}s.");
        }
        finally
        {
            _pendingRequests.TryRemove(requestMsg.Id, out _);
        }
    }

    private async Task ReadLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested && _stream != null && _stream.IsConnected)
            {
                var message = await IpcMessageFramer.ReadMessageAsync(_stream, stoppingToken).ConfigureAwait(false);
                if (message == null)
                {
                    // Clean EOF from server
                    break;
                }

                if (message.Type == "Response")
                {
                    if (_pendingRequests.TryRemove(message.Id, out var tcs))
                    {
                        tcs.TrySetResult(message);
                    }
                }
                else if (message.Type == "Event")
                {
                    try
                    {
                        EventReceived?.Invoke(this, message);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Exception in IPC EventReceived handler for action '{Action}'.", message.Action);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "IPC client read loop encountered error or disconnection.");
        }
        finally
        {
            _isConnected = false;
            foreach (var (id, tcs) in _pendingRequests)
            {
                tcs.TrySetCanceled();
            }
            _pendingRequests.Clear();
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cts?.Cancel();
        _cts?.Dispose();
        _stream?.Dispose();
        _sendLock.Dispose();

        foreach (var (id, tcs) in _pendingRequests)
        {
            tcs.TrySetCanceled();
        }
        _pendingRequests.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        await DisconnectAsync().ConfigureAwait(false);
        Dispose();
    }
}

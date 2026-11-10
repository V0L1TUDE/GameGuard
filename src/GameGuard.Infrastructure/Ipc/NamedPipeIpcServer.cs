using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using GameGuard.Core.Ipc;
using Microsoft.Extensions.Logging;

namespace GameGuard.Infrastructure.Ipc;

public sealed class NamedPipeIpcServer : IIpcServer
{
    public const string DefaultPipeName = "GameGuard_IPC_Pipe";

    private readonly string _pipeName;
    private readonly ILogger<NamedPipeIpcServer>? _logger;
    private readonly ConcurrentDictionary<string, Func<IpcMessage, Task<IpcMessage>>> _handlers = new();
    private readonly ConcurrentDictionary<NamedPipeServerStream, SemaphoreSlim> _activeClients = new();

    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private bool _isDisposed;

    public bool IsRunning => _listenerTask != null && !_listenerTask.IsCompleted;
    public int ConnectedClientCount => _activeClients.Count;

    public NamedPipeIpcServer(string pipeName = DefaultPipeName, ILogger<NamedPipeIpcServer>? logger = null)
    {
        _pipeName = pipeName;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return Task.CompletedTask;

        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token), CancellationToken.None);
        _logger?.LogInformation("Named Pipe IPC Server started on pipe '{PipeName}'.", _pipeName);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts == null) return;

        _logger?.LogInformation("Named Pipe IPC Server stopping...");
        _cts.Cancel();

        // Disconnect and clean up all active clients
        foreach (var (stream, sem) in _activeClients)
        {
            try
            {
                stream.Disconnect();
                stream.Dispose();
            }
            catch
            {
            }
        }
        _activeClients.Clear();

        if (_listenerTask != null)
        {
            try
            {
                await _listenerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Exception while awaiting IPC listener task completion.");
            }
        }

        _logger?.LogInformation("Named Pipe IPC Server stopped cleanly.");
    }

    public void RegisterHandler(string action, Func<IpcMessage, Task<IpcMessage>> handler)
    {
        _handlers[action] = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public async Task BroadcastEventAsync(string action, object? payload, CancellationToken cancellationToken = default)
    {
        if (_activeClients.IsEmpty) return;

        string? json = payload != null ? JsonSerializer.Serialize(payload) : null;
        var eventMsg = IpcMessage.CreateEvent(action, json);

        var tasks = new List<Task>();
        foreach (var (clientStream, sem) in _activeClients)
        {
            tasks.Add(Task.Run(async () =>
            {
                await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (clientStream.IsConnected)
                    {
                        await IpcMessageFramer.WriteMessageAsync(clientStream, eventMsg, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Failed to broadcast IPC event to a connected client.");
                }
                finally
                {
                    sem.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task ListenLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? serverStream = null;
            try
            {
                serverStream = CreateServerStream(_pipeName);
                await serverStream.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);

                _logger?.LogInformation("IPC client connected to pipe '{PipeName}'.", _pipeName);
                var clientLock = new SemaphoreSlim(1, 1);
                _activeClients[serverStream] = clientLock;

                // Handle client communication in background task
                _ = Task.Run(() => HandleClientAsync(serverStream, clientLock, stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                serverStream?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error accepting IPC client connection on pipe '{PipeName}'. Backing off.", _pipeName);
                serverStream?.Dispose();
                try
                {
                    await Task.Delay(500, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, SemaphoreSlim sem, CancellationToken stoppingToken)
    {
        try
        {
            while (stream.IsConnected && !stoppingToken.IsCancellationRequested)
            {
                var request = await IpcMessageFramer.ReadMessageAsync(stream, stoppingToken).ConfigureAwait(false);
                if (request == null)
                {
                    // Clean EOF from client
                    break;
                }

                if (request.Type == "Request")
                {
                    IpcMessage response;
                    if (_handlers.TryGetValue(request.Action, out var handler))
                    {
                        try
                        {
                            response = await handler(request).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error executing IPC handler for action '{Action}'.", request.Action);
                            var errorPayload = JsonSerializer.Serialize(new CommandResultResponse(false, ex.Message));
                            response = IpcMessage.CreateResponse(request.Id, request.Action, errorPayload);
                        }
                    }
                    else
                    {
                        var notFoundPayload = JsonSerializer.Serialize(new CommandResultResponse(false, $"Unknown IPC action '{request.Action}'."));
                        response = IpcMessage.CreateResponse(request.Id, request.Action, notFoundPayload);
                    }

                    await sem.WaitAsync(stoppingToken).ConfigureAwait(false);
                    try
                    {
                        if (stream.IsConnected)
                        {
                            await IpcMessageFramer.WriteMessageAsync(stream, response, stoppingToken).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        sem.Release();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Client connection terminated on pipe '{PipeName}'.", _pipeName);
        }
        finally
        {
            _activeClients.TryRemove(stream, out _);
            try
            {
                if (stream.IsConnected)
                {
                    stream.Disconnect();
                }
            }
            catch
            {
            }
            stream.Dispose();
            sem.Dispose();
            _logger?.LogInformation("IPC client disconnected from pipe '{PipeName}'. Active clients: {Count}.", _pipeName, _activeClients.Count);
        }
    }

    private static NamedPipeServerStream CreateServerStream(string pipeName)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var pipeSecurity = new PipeSecurity();
                // Allow current user / authenticated users read & write access
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                    PipeAccessRights.ReadWrite,
                    AccessControlType.Allow));
                // Allow Administrators & Local System full control
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    PipeAccessRights.FullControl,
                    AccessControlType.Allow));
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    PipeAccessRights.FullControl,
                    AccessControlType.Allow));

                return NamedPipeServerStreamAcl.Create(
                    pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    pipeSecurity);
            }
            catch
            {
                // Fallback to standard creation if ACL cannot be set
            }
        }

        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cts?.Cancel();
        _cts?.Dispose();

        foreach (var (stream, sem) in _activeClients)
        {
            try
            {
                stream.Dispose();
                sem.Dispose();
            }
            catch
            {
            }
        }
        _activeClients.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        await StopAsync().ConfigureAwait(false);
        Dispose();
    }
}

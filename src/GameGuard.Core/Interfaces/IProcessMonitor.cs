using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface IProcessMonitor : IAsyncDisposable, IDisposable
{
    event EventHandler<ProcessLifecycleEventArgs>? ProcessStarted;
    event EventHandler<ProcessLifecycleEventArgs>? ProcessRunning;
    event EventHandler<ProcessLifecycleEventArgs>? ProcessExited;

    event Func<object?, ProcessLifecycleEventArgs, Task>? ProcessStartedAsync;
    event Func<object?, ProcessLifecycleEventArgs, Task>? ProcessRunningAsync;
    event Func<object?, ProcessLifecycleEventArgs, Task>? ProcessExitedAsync;

    IReadOnlyList<DetectedProcessInfo> GetActiveMonitoredProcesses();
    void RegisterTrackedProcess(int processId, GameProfile profile, DetectedProcessInfo process);
    bool IsRunning { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task CheckProcessesOnceAsync(CancellationToken cancellationToken = default);
}

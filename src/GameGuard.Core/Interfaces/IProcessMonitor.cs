using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface IProcessMonitor : IAsyncDisposable, IDisposable
{
    event EventHandler<ProcessLifecycleEventArgs>? ProcessStarted;
    event EventHandler<ProcessLifecycleEventArgs>? ProcessRunning;
    event EventHandler<ProcessLifecycleEventArgs>? ProcessExited;

    IReadOnlyList<DetectedProcessInfo> GetActiveMonitoredProcesses();
    bool IsRunning { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task CheckProcessesOnceAsync(CancellationToken cancellationToken = default);
}

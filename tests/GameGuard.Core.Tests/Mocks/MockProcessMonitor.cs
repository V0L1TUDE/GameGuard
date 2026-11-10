using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockProcessMonitor : IProcessMonitor
{
    public event EventHandler<ProcessLifecycleEventArgs>? ProcessStarted { add { } remove { } }
    public event EventHandler<ProcessLifecycleEventArgs>? ProcessRunning { add { } remove { } }
    public event EventHandler<ProcessLifecycleEventArgs>? ProcessExited { add { } remove { } }

    public event Func<object?, ProcessLifecycleEventArgs, Task>? ProcessStartedAsync { add { } remove { } }
    public event Func<object?, ProcessLifecycleEventArgs, Task>? ProcessRunningAsync { add { } remove { } }
    public event Func<object?, ProcessLifecycleEventArgs, Task>? ProcessExitedAsync { add { } remove { } }

    public bool IsRunning { get; private set; }
    public bool StartCalled { get; private set; }
    public bool StopCalled { get; private set; }

    public IReadOnlyList<DetectedProcessInfo> GetActiveMonitoredProcesses() => Array.Empty<DetectedProcessInfo>();

    public void RegisterTrackedProcess(int processId, GameProfile profile, DetectedProcessInfo process) { }

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

    public Task CheckProcessesOnceAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

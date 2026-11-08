using GameGuard.Core.Models;
using GameGuard.Core.Services;
using GameGuard.Core.Tests.Mocks;
using GameGuard.Infrastructure.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class ProcessMonitorTests
{
    private readonly MockProcessScanner _scanner = new();
    private readonly MockGameProfileRepository _gameRepo = new();
    private readonly SystemClock _clock = new();

    private readonly GameProfile _cs2Profile = new()
    {
        Id = Guid.NewGuid(),
        Name = "Counter-Strike 2",
        ProcessNames = new[] { "cs2.exe" },
        IsMonitored = true
    };

    private readonly GameProfile _valorantProfile = new()
    {
        Id = Guid.NewGuid(),
        Name = "Valorant",
        ProcessNames = new[] { "VALORANT-Win64-Shipping.exe", "VALORANT.exe" },
        IsMonitored = true
    };

    public ProcessMonitorTests()
    {
        _gameRepo.AddRange(new[] { _cs2Profile, _valorantProfile });
    }

    [Fact]
    public async Task CheckProcessesOnceAsync_WhenGameProcessAppears_FiresProcessStarted()
    {
        using var monitor = new ProcessMonitor(_scanner, _gameRepo, _clock, NullLogger<ProcessMonitor>.Instance);

        ProcessLifecycleEventArgs? capturedArgs = null;
        monitor.ProcessStarted += (_, args) => capturedArgs = args;

        _scanner.AddProcess(pid: 5001, name: "cs2.exe");

        await monitor.CheckProcessesOnceAsync();

        Assert.NotNull(capturedArgs);
        Assert.Equal(ProcessEventType.Started, capturedArgs.EventType);
        Assert.Equal("Counter-Strike 2", capturedArgs.Game.Name);
        Assert.Equal(5001, capturedArgs.Process.ProcessId);

        var active = monitor.GetActiveMonitoredProcesses();
        Assert.Single(active);
        Assert.Equal(5001, active[0].ProcessId);
    }

    [Fact]
    public async Task CheckProcessesOnceAsync_WhenGameProcessPersists_FiresProcessRunning()
    {
        using var monitor = new ProcessMonitor(_scanner, _gameRepo, _clock, NullLogger<ProcessMonitor>.Instance);

        int startEvents = 0;
        int runningEvents = 0;
        monitor.ProcessStarted += (_, _) => startEvents++;
        monitor.ProcessRunning += (_, _) => runningEvents++;

        _scanner.AddProcess(pid: 5001, name: "cs2.exe");

        // First scan: Detects start
        await monitor.CheckProcessesOnceAsync();
        Assert.Equal(1, startEvents);
        Assert.Equal(0, runningEvents);

        // Second scan: Process is still alive
        await monitor.CheckProcessesOnceAsync();
        Assert.Equal(1, startEvents);
        Assert.Equal(1, runningEvents);
    }

    [Fact]
    public async Task CheckProcessesOnceAsync_WhenGameProcessExits_FiresProcessExited()
    {
        using var monitor = new ProcessMonitor(_scanner, _gameRepo, _clock, NullLogger<ProcessMonitor>.Instance);

        ProcessLifecycleEventArgs? exitedArgs = null;
        monitor.ProcessExited += (_, args) => exitedArgs = args;

        _scanner.AddProcess(pid: 5001, name: "cs2.exe");
        await monitor.CheckProcessesOnceAsync();

        // Game terminates
        _scanner.RemoveProcess(5001);
        await monitor.CheckProcessesOnceAsync();

        Assert.NotNull(exitedArgs);
        Assert.Equal(ProcessEventType.Exited, exitedArgs.EventType);
        Assert.Equal("Counter-Strike 2", exitedArgs.Game.Name);
        Assert.Equal(5001, exitedArgs.Process.ProcessId);

        var active = monitor.GetActiveMonitoredProcesses();
        Assert.Empty(active);
    }

    [Fact]
    public async Task CheckProcessesOnceAsync_WhenUnmonitoredProcessRuns_DoesNotFireEvents()
    {
        using var monitor = new ProcessMonitor(_scanner, _gameRepo, _clock, NullLogger<ProcessMonitor>.Instance);

        bool anyEventFired = false;
        monitor.ProcessStarted += (_, _) => anyEventFired = true;
        monitor.ProcessRunning += (_, _) => anyEventFired = true;
        monitor.ProcessExited += (_, _) => anyEventFired = true;

        _scanner.AddProcess(pid: 9999, name: "notepad.exe");
        _scanner.AddProcess(pid: 8888, name: "chrome.exe");

        await monitor.CheckProcessesOnceAsync();

        Assert.False(anyEventFired);
        Assert.Empty(monitor.GetActiveMonitoredProcesses());
    }

    [Fact]
    public async Task CheckProcessesOnceAsync_WhenGameProfileIsDisabled_DoesNotFireEvents()
    {
        var disabledGame = new GameProfile
        {
            Id = Guid.NewGuid(),
            Name = "Apex Legends",
            ProcessNames = new[] { "r5apex.exe" },
            IsMonitored = false
        };
        await _gameRepo.AddAsync(disabledGame);

        using var monitor = new ProcessMonitor(_scanner, _gameRepo, _clock, NullLogger<ProcessMonitor>.Instance);

        bool startedFired = false;
        monitor.ProcessStarted += (_, _) => startedFired = true;

        _scanner.AddProcess(pid: 6001, name: "r5apex.exe");

        await monitor.CheckProcessesOnceAsync();

        Assert.False(startedFired);
        Assert.Empty(monitor.GetActiveMonitoredProcesses());
    }

    [Fact]
    public async Task CheckProcessesOnceAsync_WithMultipleGamesConcurrently_TracksIndependently()
    {
        using var monitor = new ProcessMonitor(_scanner, _gameRepo, _clock, NullLogger<ProcessMonitor>.Instance);

        var started = new List<string>();
        var exited = new List<string>();
        monitor.ProcessStarted += (_, args) => started.Add(args.Game.Name);
        monitor.ProcessExited += (_, args) => exited.Add(args.Game.Name);

        // Both games launched
        _scanner.AddProcess(pid: 1001, name: "cs2.exe");
        _scanner.AddProcess(pid: 1002, name: "VALORANT-Win64-Shipping.exe");

        await monitor.CheckProcessesOnceAsync();

        Assert.Equal(2, started.Count);
        Assert.Contains("Counter-Strike 2", started);
        Assert.Contains("Valorant", started);
        Assert.Equal(2, monitor.GetActiveMonitoredProcesses().Count);

        // CS2 exits, Valorant stays running
        _scanner.RemoveProcess(1001);
        await monitor.CheckProcessesOnceAsync();

        Assert.Single(exited);
        Assert.Equal("Counter-Strike 2", exited[0]);
        var remaining = monitor.GetActiveMonitoredProcesses();
        Assert.Single(remaining);
        Assert.Equal(1002, remaining[0].ProcessId);
    }

    [Fact]
    public async Task ProcessMonitor_StartAndStop_ManagesLifecycleCorrectly()
    {
        using var monitor = new ProcessMonitor(
            _scanner,
            _gameRepo,
            _clock,
            NullLogger<ProcessMonitor>.Instance,
            pollingInterval: TimeSpan.FromMilliseconds(50));

        Assert.False(monitor.IsRunning);

        await monitor.StartAsync();
        Assert.True(monitor.IsRunning);

        await monitor.StopAsync();
        Assert.False(monitor.IsRunning);
    }
}

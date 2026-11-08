using System.Collections.Concurrent;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Extensions.Logging;

namespace GameGuard.Infrastructure.Monitoring;

public sealed class ProcessMonitor : IProcessMonitor
{
    private sealed record TrackedProcessState(
        GameProfile Profile,
        DetectedProcessInfo Process,
        DateTime FirstDetectedUtc,
        DateTime LastSeenUtc);

    private readonly IProcessScanner _scanner;
    private readonly IGameProfileRepository _gameRepository;
    private readonly ISystemClock _clock;
    private readonly ILogger<ProcessMonitor> _logger;
    private readonly TimeSpan _pollingInterval;

    private readonly ConcurrentDictionary<int, TrackedProcessState> _trackedProcesses = new();
    private CancellationTokenSource? _cts;
    private Task? _monitoringTask;
    private int _isDisposed;

    public event EventHandler<ProcessLifecycleEventArgs>? ProcessStarted;
    public event EventHandler<ProcessLifecycleEventArgs>? ProcessRunning;
    public event EventHandler<ProcessLifecycleEventArgs>? ProcessExited;

    public bool IsRunning => _monitoringTask != null && !_monitoringTask.IsCompleted;

    public ProcessMonitor(
        IProcessScanner scanner,
        IGameProfileRepository gameRepository,
        ISystemClock clock,
        ILogger<ProcessMonitor> logger,
        TimeSpan? pollingInterval = null)
    {
        _scanner = scanner;
        _gameRepository = gameRepository;
        _clock = clock;
        _logger = logger;
        _pollingInterval = pollingInterval ?? TimeSpan.FromSeconds(1);
    }

    public IReadOnlyList<DetectedProcessInfo> GetActiveMonitoredProcesses()
    {
        return _trackedProcesses.Values
            .Select(t => t.Process)
            .ToList();
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            _logger.LogWarning("ProcessMonitor is already running.");
            return Task.CompletedTask;
        }

        _cts = new CancellationTokenSource();
        _monitoringTask = Task.Run(() => MonitoringLoopAsync(_cts.Token), CancellationToken.None);
        _logger.LogInformation("ProcessMonitor started with polling interval: {Interval} ms.", _pollingInterval.TotalMilliseconds);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _cts == null)
        {
            return;
        }

        _logger.LogInformation("Stopping ProcessMonitor...");
        _cts.Cancel();

        if (_monitoringTask != null)
        {
            try
            {
                await _monitoringTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while stopping monitoring loop.");
            }
            finally
            {
                _monitoringTask = null;
            }
        }

        _cts.Dispose();
        _cts = null;

        _logger.LogInformation("ProcessMonitor stopped.");
    }

    public async Task CheckProcessesOnceAsync(CancellationToken cancellationToken = default)
    {
        DateTime nowUtc = _clock.UtcNow;
        var monitoredGames = await _gameRepository.GetMonitoredAsync(cancellationToken).ConfigureAwait(false);

        if (monitoredGames.Count == 0)
        {
            // If no games are configured for monitoring, check if any currently tracked need exit event
            NotifyExitedProcesses(new HashSet<int>(), nowUtc);
            return;
        }

        var runningProcesses = _scanner.ScanProcesses();
        var currentMonitoredPids = new HashSet<int>();

        foreach (var process in runningProcesses)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var matchingGame = monitoredGames.FirstOrDefault(g => g.MatchesProcess(process.ProcessName));
            if (matchingGame == null)
            {
                continue;
            }

            currentMonitoredPids.Add(process.ProcessId);

            if (_trackedProcesses.TryGetValue(process.ProcessId, out var existingState))
            {
                var updated = existingState with { LastSeenUtc = nowUtc };
                _trackedProcesses[process.ProcessId] = updated;

                SafeRaiseEvent(ProcessRunning, new ProcessLifecycleEventArgs
                {
                    Game = matchingGame,
                    Process = process,
                    EventType = ProcessEventType.Running,
                    TimestampUtc = nowUtc
                });
            }
            else
            {
                var newState = new TrackedProcessState(matchingGame, process, nowUtc, nowUtc);
                _trackedProcesses[process.ProcessId] = newState;

                _logger.LogInformation(
                    "Monitored game started: {GameName} (PID: {Pid}, Exe: {Exe})",
                    matchingGame.Name,
                    process.ProcessId,
                    process.ProcessName);

                SafeRaiseEvent(ProcessStarted, new ProcessLifecycleEventArgs
                {
                    Game = matchingGame,
                    Process = process,
                    EventType = ProcessEventType.Started,
                    TimestampUtc = nowUtc
                });
            }
        }

        NotifyExitedProcesses(currentMonitoredPids, nowUtc);
    }

    private void NotifyExitedProcesses(HashSet<int> currentMonitoredPids, DateTime nowUtc)
    {
        var exitedPids = _trackedProcesses.Keys
            .Where(pid => !currentMonitoredPids.Contains(pid))
            .ToList();

        foreach (var pid in exitedPids)
        {
            if (_trackedProcesses.TryRemove(pid, out var removedState))
            {
                _logger.LogInformation(
                    "Monitored game exited: {GameName} (PID: {Pid}, Exe: {Exe})",
                    removedState.Profile.Name,
                    removedState.Process.ProcessId,
                    removedState.Process.ProcessName);

                SafeRaiseEvent(ProcessExited, new ProcessLifecycleEventArgs
                {
                    Game = removedState.Profile,
                    Process = removedState.Process,
                    EventType = ProcessEventType.Exited,
                    TimestampUtc = nowUtc
                });
            }
        }
    }

    private async Task MonitoringLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckProcessesOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in process monitoring loop cycle.");
            }

            try
            {
                await Task.Delay(_pollingInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void SafeRaiseEvent(EventHandler<ProcessLifecycleEventArgs>? handler, ProcessLifecycleEventArgs args)
    {
        if (handler == null)
        {
            return;
        }

        foreach (var invocation in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<ProcessLifecycleEventArgs>)invocation).Invoke(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in ProcessMonitor event handler for event {EventType}.", args.EventType);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _trackedProcesses.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _trackedProcesses.Clear();
    }
}

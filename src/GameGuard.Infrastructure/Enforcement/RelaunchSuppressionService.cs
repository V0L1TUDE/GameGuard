using System.Collections.Concurrent;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Extensions.Logging;

namespace GameGuard.Infrastructure.Enforcement;

public sealed class RelaunchSuppressionService : IRelaunchSuppressionService
{
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(5);
    private const int MaxAttemptsBeforeCooldown = 3;

    private readonly IProcessMonitor _processMonitor;
    private readonly ILimitManagerService _limitManager;
    private readonly IProcessTerminator _processTerminator;
    private readonly IRelaunchBlockRepository _blockRepository;
    private readonly ISessionRepository _sessionRepository;
    private readonly ITimeTrackerService _timeTracker;
    private readonly ISystemClock _systemClock;
    private readonly ILogger<RelaunchSuppressionService> _logger;

    private readonly ConcurrentDictionary<Guid, List<DateTime>> _recentAttempts = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _cooldowns = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _isDisposed;
    private bool _isRunning;

    public event EventHandler<RelaunchBlockedEventArgs>? RelaunchBlocked;

    public bool IsRunning => _isRunning;

    public RelaunchSuppressionService(
        IProcessMonitor processMonitor,
        ILimitManagerService limitManager,
        IProcessTerminator processTerminator,
        IRelaunchBlockRepository blockRepository,
        ISessionRepository sessionRepository,
        ITimeTrackerService timeTracker,
        ISystemClock systemClock,
        ILogger<RelaunchSuppressionService> logger)
    {
        _processMonitor = processMonitor;
        _limitManager = limitManager;
        _processTerminator = processTerminator;
        _blockRepository = blockRepository;
        _sessionRepository = sessionRepository;
        _timeTracker = timeTracker;
        _systemClock = systemClock;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return Task.CompletedTask;
        }

        _processMonitor.ProcessStarted += OnProcessStarted;
        _isRunning = true;
        _logger.LogInformation("RelaunchSuppressionService started and monitoring game starts.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return Task.CompletedTask;
        }

        _processMonitor.ProcessStarted -= OnProcessStarted;
        _isRunning = false;
        _logger.LogInformation("RelaunchSuppressionService stopped.");

        return Task.CompletedTask;
    }

    private void OnProcessStarted(object? sender, ProcessLifecycleEventArgs args)
    {
        _ = CheckAndSuppressIfExhaustedAsync(args.Game, args.Process.ProcessId, args.Process.ProcessName);
    }

    public bool IsInCooldown(Guid gameProfileId, out TimeSpan remainingCooldown)
    {
        DateTime nowUtc = _systemClock.UtcNow;
        if (_cooldowns.TryGetValue(gameProfileId, out var cooldownUntilUtc) && nowUtc < cooldownUntilUtc)
        {
            remainingCooldown = cooldownUntilUtc - nowUtc;
            return true;
        }

        remainingCooldown = TimeSpan.Zero;
        return false;
    }

    public async Task<bool> CheckAndSuppressIfExhaustedAsync(
        GameProfile game,
        int processId,
        string processName,
        CancellationToken cancellationToken = default)
    {
        DateTime nowUtc = _systemClock.UtcNow;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool inCooldown = IsInCooldown(game.Id, out var remainingCooldown);
            var status = await _limitManager.GetCurrentStatusAsync(game.Id, cancellationToken).ConfigureAwait(false);

            if (!inCooldown && !status.IsLimitExceeded)
            {
                return false;
            }

            string reason = inCooldown
                ? $"Relaunch blocked: Cooldown active ({Math.Ceiling(remainingCooldown.TotalSeconds)}s remaining due to repeated attempts)."
                : "Relaunch blocked: Daily gaming limit is exhausted.";

            _logger.LogWarning(
                "Suppressing relaunch of {GameName} (PID: {Pid}, Process: {ProcessName}). Reason: {Reason}",
                game.Name,
                processId,
                processName,
                reason);

            // Fast process termination to minimize game startup display
            await _processTerminator.TerminateProcessAsync(
                processId,
                processName,
                gracefulTimeout: TimeSpan.FromMilliseconds(50),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Reconcile any newly created active session in SQLite / TimeTracker
            var activeSession = _timeTracker.GetActiveSession(processId);
            if (activeSession != null)
            {
                try
                {
                    await _sessionRepository.EndSessionAsync(
                        activeSession.Id,
                        nowUtc,
                        SessionTerminationReason.LimitEnforced,
                        notes: reason,
                        durationSeconds: 0,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update session status for suppressed PID {Pid}.", processId);
                }
            }

            // Update sliding window attempt counters
            var attempts = _recentAttempts.GetOrAdd(game.Id, _ => new List<DateTime>());
            attempts.RemoveAll(t => (nowUtc - t) > AttemptWindow);
            attempts.Add(nowUtc);

            int attemptCount = attempts.Count;
            if (attemptCount >= MaxAttemptsBeforeCooldown && !inCooldown)
            {
                _cooldowns[game.Id] = nowUtc.Add(DefaultCooldown);
                remainingCooldown = DefaultCooldown;
                _logger.LogCritical(
                    "Excessive relaunch attempts detected for {GameName} ({Count} attempts in {WindowSeconds}s). Enforcing {CooldownMinutes}m cooldown.",
                    game.Name,
                    attemptCount,
                    AttemptWindow.TotalSeconds,
                    DefaultCooldown.TotalMinutes);
            }

            // Persist block audit log
            var blockRecord = new RelaunchBlockRecord
            {
                Id = Guid.NewGuid(),
                GameProfileId = game.Id,
                ProcessId = processId,
                ProcessName = processName,
                BlockedAtUtc = nowUtc,
                Reason = reason
            };

            try
            {
                await _blockRepository.RecordBlockAsync(blockRecord, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record relaunch block in repository.");
            }

            // Raise notification event
            SafeRaiseEvent(RelaunchBlocked, new RelaunchBlockedEventArgs
            {
                Game = game,
                ProcessId = processId,
                ProcessName = processName,
                BlockedAtUtc = nowUtc,
                AttemptCountInWindow = attemptCount,
                CooldownRemaining = remainingCooldown > TimeSpan.Zero ? remainingCooldown : null,
                Reason = reason
            });

            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<IReadOnlyList<RelaunchBlockRecord>> GetRecentBlockedRelaunchesAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        return _blockRepository.GetRecentBlocksAsync(limit, cancellationToken);
    }

    private void SafeRaiseEvent<T>(EventHandler<T>? handler, T args) where T : EventArgs
    {
        if (handler == null)
        {
            return;
        }

        foreach (var invocation in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<T>)invocation).Invoke(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing RelaunchSuppressionService event handler.");
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _processMonitor.ProcessStarted -= OnProcessStarted;
        _lock.Dispose();
        _recentAttempts.Clear();
        _cooldowns.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

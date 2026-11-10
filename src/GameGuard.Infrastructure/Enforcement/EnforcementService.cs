using System.Collections.Concurrent;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Extensions.Logging;

namespace GameGuard.Infrastructure.Enforcement;

public sealed class EnforcementService : IEnforcementService
{
    private readonly ILimitManagerService _limitManager;
    private readonly ITimeTrackerService _timeTracker;
    private readonly IGameProfileRepository _gameRepository;
    private readonly ISessionRepository _sessionRepository;
    private readonly IProcessTerminator _processTerminator;
    private readonly IProcessScanner _processScanner;
    private readonly ISystemClock _systemClock;
    private readonly ILogger<EnforcementService> _logger;

    private readonly ConcurrentDictionary<int, EnforcementResult> _recentEnforcements = new();
    private readonly SemaphoreSlim _enforcementLock = new(1, 1);
    private int _isDisposed;
    private bool _isRunning;

    public event EventHandler<GameEnforcedEventArgs>? GameEnforced;

    public bool IsRunning => _isRunning;

    public EnforcementService(
        ILimitManagerService limitManager,
        ITimeTrackerService timeTracker,
        IGameProfileRepository gameRepository,
        ISessionRepository sessionRepository,
        IProcessTerminator processTerminator,
        IProcessScanner processScanner,
        ISystemClock systemClock,
        ILogger<EnforcementService> logger)
    {
        _limitManager = limitManager;
        _timeTracker = timeTracker;
        _gameRepository = gameRepository;
        _sessionRepository = sessionRepository;
        _processTerminator = processTerminator;
        _processScanner = processScanner;
        _systemClock = systemClock;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return Task.CompletedTask;
        }

        _limitManager.QuotaExhausted += OnQuotaExhausted;
        _isRunning = true;
        _logger.LogInformation("EnforcementService started and listening for quota exhaustion events.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return Task.CompletedTask;
        }

        _limitManager.QuotaExhausted -= OnQuotaExhausted;
        _isRunning = false;
        _logger.LogInformation("EnforcementService stopped.");

        return Task.CompletedTask;
    }

    private void OnQuotaExhausted(object? sender, QuotaExhaustedEventArgs args)
    {
        _ = HandleQuotaExhaustedAsync(args);
    }

    private async Task HandleQuotaExhaustedAsync(QuotaExhaustedEventArgs args)
    {
        var activeSessions = _timeTracker.GetActiveSessions()
            .Where(s => s.GameProfileId == args.Game.Id)
            .ToList();

        if (activeSessions.Count == 0)
        {
            _logger.LogInformation("Quota exhausted for {GameName}, but no active sessions were found to enforce.", args.Game.Name);
            return;
        }

        foreach (var session in activeSessions)
        {
            await EnforceGameAsync(args.Game, session.ProcessId, session.ProcessName).ConfigureAwait(false);
        }
    }

    public async Task<EnforcementResult> EnforceGameAsync(
        GameProfile game,
        int processId,
        string processName,
        TimeSpan? gracefulTimeout = null,
        CancellationToken cancellationToken = default)
    {
        DateTime nowUtc = _systemClock.UtcNow;

        // Security check 1: Disallow terminating critical system PIDs
        if (processId <= 4)
        {
            _logger.LogCritical("Refusing to enforce termination on critical system PID {Pid}.", processId);
            return CreateResult(game, processId, processName, false, EnforcementMethod.Failed, nowUtc, "Protected system PID.");
        }

        await _enforcementLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Security check 2: Reused PID safeguard.
            // Verify current process list to ensure the PID still exists AND is actually the target game!
            var currentProcesses = _processScanner.ScanProcesses();
            var targetProcess = currentProcesses.FirstOrDefault(p => p.ProcessId == processId);

            if (targetProcess == null)
            {
                _logger.LogInformation("Target process PID {Pid} has already exited before enforcement.", processId);
                return CreateResult(game, processId, processName, true, EnforcementMethod.GracefulClose, nowUtc, null);
            }

            if (!game.MatchesProcess(targetProcess.ProcessName))
            {
                _logger.LogCritical(
                    "SAFETY ABORT: PID {Pid} ({ActualName}) does not match configured game {GameName} ({ExpectedExe}). Reused PID safeguard triggered.",
                    processId,
                    targetProcess.ProcessName,
                    game.Name,
                    processName);

                return CreateResult(
                    game,
                    processId,
                    targetProcess.ProcessName,
                    false,
                    EnforcementMethod.Failed,
                    nowUtc,
                    "Reused PID safeguard triggered: process name mismatch.");
            }

            // Execute termination (graceful close attempt followed by forced termination)
            TimeSpan timeout = gracefulTimeout ?? TimeSpan.FromSeconds(3);
            var (success, method) = await _processTerminator.TerminateProcessAsync(
                processId,
                targetProcess.ProcessName,
                timeout,
                cancellationToken).ConfigureAwait(false);

            if (success)
            {
                // Update SQLite session record with LimitEnforced termination reason
                var activeSession = _timeTracker.GetActiveSession(processId);
                if (activeSession != null)
                {
                    activeSession.TerminationReason = SessionTerminationReason.LimitEnforced;
                    try
                    {
                        await _sessionRepository.EndSessionAsync(
                            activeSession.Id,
                            nowUtc,
                            SessionTerminationReason.LimitEnforced,
                            notes: "Terminated automatically by GameGuard enforcement engine upon daily limit exhaustion.",
                            durationSeconds: activeSession.DurationSeconds,
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to persist LimitEnforced status for session {SessionId}.", activeSession.Id);
                    }
                }
            }

            var result = CreateResult(
                game,
                processId,
                targetProcess.ProcessName,
                success,
                method,
                nowUtc,
                success ? null : "Process termination failed.");

            _recentEnforcements[processId] = result;

            SafeRaiseEvent(GameEnforced, new GameEnforcedEventArgs { Result = result });
            return result;
        }
        finally
        {
            _enforcementLock.Release();
        }
    }

    private static EnforcementResult CreateResult(
        GameProfile game,
        int processId,
        string processName,
        bool success,
        EnforcementMethod method,
        DateTime timestampUtc,
        string? errorMessage)
    {
        return new EnforcementResult
        {
            GameProfileId = game.Id,
            GameName = game.Name,
            ProcessId = processId,
            ProcessName = processName,
            Success = success,
            MethodUsed = method,
            TimestampUtc = timestampUtc,
            ErrorMessage = errorMessage
        };
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
                _logger.LogError(ex, "Error in EnforcementService event handler for {EventType}.", typeof(T).Name);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _limitManager.QuotaExhausted -= OnQuotaExhausted;
        _enforcementLock.Dispose();
        _recentEnforcements.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

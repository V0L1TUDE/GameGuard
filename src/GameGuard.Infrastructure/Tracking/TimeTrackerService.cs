using System.Collections.Concurrent;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Extensions.Logging;

namespace GameGuard.Infrastructure.Tracking;

public sealed class TimeTrackerService : ITimeTrackerService
{
    private sealed class ActiveSessionTracker
    {
        public required GamingSession Session { get; init; }
        public required GameProfile GameProfile { get; init; }
        public required long StartTimestamp { get; init; }
        public long LastTickTimestamp { get; set; }
        public long LastPersistedTimestamp { get; set; }
        public int PersistedDurationSeconds { get; set; }
        public double AccumulatedSeconds { get; set; }
        public object SyncLock { get; } = new();
    }

    private readonly IProcessMonitor _processMonitor;
    private readonly ISessionRepository _sessionRepository;
    private readonly IGameProfileRepository _gameRepository;
    private readonly IProcessScanner _processScanner;
    private readonly ISystemClock _systemClock;
    private readonly IMonotonicClock _monotonicClock;
    private readonly ILogger<TimeTrackerService> _logger;
    private readonly TimeSpan _checkpointInterval;
    private readonly TimeSpan _suspensionGapThreshold;

    private readonly ConcurrentDictionary<int, ActiveSessionTracker> _activeTrackers = new();
    private readonly SemaphoreSlim _dbWriteLock = new(1, 1);
    private int _isDisposed;

    public event EventHandler<SessionLifecycleEventArgs>? SessionStarted;
    public event EventHandler<SessionLifecycleEventArgs>? SessionUpdated;
    public event EventHandler<SessionLifecycleEventArgs>? SessionEnded;

    public TimeTrackerService(
        IProcessMonitor processMonitor,
        ISessionRepository sessionRepository,
        IGameProfileRepository gameRepository,
        IProcessScanner processScanner,
        ISystemClock systemClock,
        IMonotonicClock monotonicClock,
        ILogger<TimeTrackerService> logger,
        TimeSpan? checkpointInterval = null,
        TimeSpan? suspensionGapThreshold = null)
    {
        _processMonitor = processMonitor;
        _sessionRepository = sessionRepository;
        _gameRepository = gameRepository;
        _processScanner = processScanner;
        _systemClock = systemClock;
        _monotonicClock = monotonicClock;
        _logger = logger;
        _checkpointInterval = checkpointInterval ?? TimeSpan.FromSeconds(15);
        _suspensionGapThreshold = suspensionGapThreshold ?? TimeSpan.FromSeconds(10);

        _processMonitor.ProcessStartedAsync += HandleProcessStartedAsync;
        _processMonitor.ProcessRunningAsync += HandleProcessRunningAsync;
        _processMonitor.ProcessExitedAsync += HandleProcessExitedAsync;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await ReconcileOrphanedSessionsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReconcileOrphanedSessionsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reconciling orphaned/active gaming sessions on startup...");

        var activeSessionsInDb = await _sessionRepository.GetActiveSessionsAsync(cancellationToken).ConfigureAwait(false);
        if (activeSessionsInDb.Count == 0)
        {
            _logger.LogInformation("No orphaned sessions found in database.");
            return;
        }

        var runningProcesses = _processScanner.ScanProcesses();
        var allGames = await _gameRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        foreach (var session in activeSessionsInDb)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var gameProfile = allGames.FirstOrDefault(g => g.Id == session.GameProfileId);
            var runningMatch = runningProcesses.FirstOrDefault(p => p.ProcessId == session.ProcessId);

            bool isProcessStillRunning = false;
            if (runningMatch != null && gameProfile != null)
            {
                // Verify process identity to guard against PID reuse
                if (gameProfile.MatchesProcess(runningMatch.ProcessName))
                {
                    isProcessStillRunning = true;
                }
            }

            if (isProcessStillRunning)
            {
                // Recover session into tracking engine
                long nowTimestamp = _monotonicClock.GetTimestamp();
                var tracker = new ActiveSessionTracker
                {
                    Session = session,
                    GameProfile = gameProfile!,
                    StartTimestamp = nowTimestamp,
                    LastTickTimestamp = nowTimestamp,
                    LastPersistedTimestamp = nowTimestamp,
                    PersistedDurationSeconds = session.DurationSeconds,
                    AccumulatedSeconds = session.DurationSeconds
                };

                if (_activeTrackers.TryAdd(session.ProcessId, tracker))
                {
                    // Register with ProcessMonitor so it recognizes the process as already running
                    _processMonitor.RegisterTrackedProcess(session.ProcessId, gameProfile!, runningMatch!);

                    _logger.LogInformation(
                        "Recovered ongoing session {SessionId} for {GameName} (PID: {Pid}, Duration: {Duration}s).",
                        session.Id,
                        gameProfile!.Name,
                        session.ProcessId,
                        session.DurationSeconds);
                }
            }
            else
            {
                // Stale session whose process is gone or PID was reused by another application
                DateTime estimatedEnd = session.StartTimeUtc.AddSeconds(session.DurationSeconds);
                if (estimatedEnd > _systemClock.UtcNow)
                {
                    estimatedEnd = _systemClock.UtcNow;
                }

                await _dbWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await _sessionRepository.EndSessionAsync(
                        session.Id,
                        estimatedEnd,
                        SessionTerminationReason.CrashOrExternal,
                        durationSeconds: session.DurationSeconds,
                        notes: "Finalized during startup reconciliation (process was no longer running).",
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _dbWriteLock.Release();
                }

                _logger.LogInformation(
                    "Finalized stale session {SessionId} for PID {Pid} (Estimated End: {End:u}).",
                    session.Id,
                    session.ProcessId,
                    estimatedEnd);
            }
        }
    }

    public IReadOnlyList<GamingSession> GetActiveSessions()
    {
        return _activeTrackers.Values
            .Select(t => t.Session)
            .ToList();
    }

    public GamingSession? GetActiveSession(int processId)
    {
        return _activeTrackers.TryGetValue(processId, out var tracker)
            ? tracker.Session
            : null;
    }

    private async Task HandleProcessStartedAsync(object? sender, ProcessLifecycleEventArgs args)
    {
        if (_activeTrackers.TryGetValue(args.Process.ProcessId, out var existing))
        {
            existing.LastTickTimestamp = _monotonicClock.GetTimestamp();
            return;
        }

        var session = new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = args.Game.Id,
            ProcessId = args.Process.ProcessId,
            ProcessName = args.Process.ProcessName,
            StartTimeUtc = args.TimestampUtc,
            DurationSeconds = 0,
            TerminationReason = SessionTerminationReason.Active
        };

        await _dbWriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _sessionRepository.StartSessionAsync(session).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist start of session {SessionId} for {GameName}.", session.Id, args.Game.Name);
            return;
        }
        finally
        {
            _dbWriteLock.Release();
        }

        long currentTimestamp = _monotonicClock.GetTimestamp();
        var tracker = new ActiveSessionTracker
        {
            Session = session,
            GameProfile = args.Game,
            StartTimestamp = currentTimestamp,
            LastTickTimestamp = currentTimestamp,
            LastPersistedTimestamp = currentTimestamp,
            PersistedDurationSeconds = 0,
            AccumulatedSeconds = 0
        };

        if (_activeTrackers.TryAdd(args.Process.ProcessId, tracker))
        {
            _logger.LogInformation(
                "Started tracking gaming session {SessionId} for {GameName} (PID: {Pid}).",
                session.Id,
                args.Game.Name,
                args.Process.ProcessId);

            SafeRaiseEvent(SessionStarted, new SessionLifecycleEventArgs
            {
                Game = args.Game,
                Session = session,
                TimestampUtc = args.TimestampUtc,
                Duration = TimeSpan.Zero
            });
        }
    }

    private async Task HandleProcessRunningAsync(object? sender, ProcessLifecycleEventArgs args)
    {
        if (!_activeTrackers.TryGetValue(args.Process.ProcessId, out var tracker))
        {
            return;
        }

        bool shouldPersist;
        int currentDurationSeconds;
        GamingSession session;
        GameProfile gameProfile;

        lock (tracker.SyncLock)
        {
            long nowTimestamp = _monotonicClock.GetTimestamp();
            TimeSpan delta = _monotonicClock.GetElapsedTime(tracker.LastTickTimestamp, nowTimestamp);
            tracker.LastTickTimestamp = nowTimestamp;

            // Detect Windows sleep or hibernation gap
            if (delta > _suspensionGapThreshold)
            {
                _logger.LogWarning(
                    "Suspension or sleep gap of {GapSeconds:F1}s detected for {GameName} (PID: {Pid}). Clamping elapsed time delta.",
                    delta.TotalSeconds,
                    args.Game.Name,
                    args.Process.ProcessId);

                delta = TimeSpan.FromSeconds(1);
            }

            tracker.AccumulatedSeconds += delta.TotalSeconds;
            currentDurationSeconds = (int)Math.Round(tracker.AccumulatedSeconds);
            tracker.Session.DurationSeconds = currentDurationSeconds;

            TimeSpan timeSinceLastPersist = _monotonicClock.GetElapsedTime(tracker.LastPersistedTimestamp, nowTimestamp);
            shouldPersist = timeSinceLastPersist >= _checkpointInterval ||
                            (currentDurationSeconds - tracker.PersistedDurationSeconds) >= _checkpointInterval.TotalSeconds;

            if (shouldPersist)
            {
                tracker.LastPersistedTimestamp = nowTimestamp;
                tracker.PersistedDurationSeconds = currentDurationSeconds;
            }

            session = tracker.Session;
            gameProfile = tracker.GameProfile;
        }

        if (shouldPersist)
        {
            await _dbWriteLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await _sessionRepository.UpdateSessionHeartbeatAsync(session.Id, currentDurationSeconds).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist heartbeat update for session {SessionId}.", session.Id);
            }
            finally
            {
                _dbWriteLock.Release();
            }

            SafeRaiseEvent(SessionUpdated, new SessionLifecycleEventArgs
            {
                Game = gameProfile,
                Session = session,
                TimestampUtc = args.TimestampUtc,
                Duration = TimeSpan.FromSeconds(currentDurationSeconds)
            });
        }
    }

    private async Task HandleProcessExitedAsync(object? sender, ProcessLifecycleEventArgs args)
    {
        if (!_activeTrackers.TryRemove(args.Process.ProcessId, out var tracker))
        {
            return;
        }

        int finalDurationSeconds;
        GamingSession session;
        GameProfile gameProfile;

        lock (tracker.SyncLock)
        {
            long nowTimestamp = _monotonicClock.GetTimestamp();
            TimeSpan delta = _monotonicClock.GetElapsedTime(tracker.LastTickTimestamp, nowTimestamp);

            if (delta > _suspensionGapThreshold)
            {
                delta = TimeSpan.FromSeconds(1);
            }

            tracker.AccumulatedSeconds += delta.TotalSeconds;
            finalDurationSeconds = Math.Max(tracker.PersistedDurationSeconds, (int)Math.Round(tracker.AccumulatedSeconds));

            var terminationReason = tracker.Session.TerminationReason != SessionTerminationReason.Active
                ? tracker.Session.TerminationReason
                : SessionTerminationReason.UserClosed;

            tracker.Session.DurationSeconds = finalDurationSeconds;
            tracker.Session.EndTimeUtc = args.TimestampUtc;
            tracker.Session.TerminationReason = terminationReason;

            session = tracker.Session;
            gameProfile = tracker.GameProfile;
        }

        await _dbWriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _sessionRepository.EndSessionAsync(
                session.Id,
                session.EndTimeUtc!.Value,
                session.TerminationReason,
                durationSeconds: finalDurationSeconds).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist termination of session {SessionId}.", session.Id);
        }
        finally
        {
            _dbWriteLock.Release();
        }

        _logger.LogInformation(
            "Finalized gaming session {SessionId} for {GameName} (PID: {Pid}, Final Duration: {Duration}s).",
            session.Id,
            gameProfile.Name,
            args.Process.ProcessId,
            finalDurationSeconds);

        SafeRaiseEvent(SessionEnded, new SessionLifecycleEventArgs
        {
            Game = gameProfile,
            Session = session,
            TimestampUtc = args.TimestampUtc,
            Duration = TimeSpan.FromSeconds(finalDurationSeconds),
            TerminationReason = SessionTerminationReason.UserClosed
        });
    }

    private void SafeRaiseEvent(EventHandler<SessionLifecycleEventArgs>? handler, SessionLifecycleEventArgs args)
    {
        if (handler == null)
        {
            return;
        }

        foreach (var invocation in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<SessionLifecycleEventArgs>)invocation).Invoke(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in session event handler for session {SessionId}.", args.Session.Id);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _processMonitor.ProcessStartedAsync -= HandleProcessStartedAsync;
        _processMonitor.ProcessRunningAsync -= HandleProcessRunningAsync;
        _processMonitor.ProcessExitedAsync -= HandleProcessExitedAsync;
        _dbWriteLock.Dispose();
        _activeTrackers.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

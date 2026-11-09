using System.Collections.Concurrent;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Extensions.Logging;

namespace GameGuard.Infrastructure.Limits;

public sealed class LimitManagerService : ILimitManagerService
{
    private readonly IGameProfileRepository _gameRepository;
    private readonly ILimitRepository _limitRepository;
    private readonly ISessionRepository _sessionRepository;
    private readonly ITimeTrackerService _timeTracker;
    private readonly ILimitCalculator _limitCalculator;
    private readonly IDayBoundaryCalculator _boundaryCalculator;
    private readonly ISystemClock _systemClock;
    private readonly ILogger<LimitManagerService> _logger;

    private readonly TimeZoneInfo _timeZone;
    private readonly IReadOnlyList<int> _warningThresholdsMinutes;
    private readonly TimeSpan _evaluationInterval;

    private readonly ConcurrentDictionary<Guid, HashSet<int>> _firedWarnings = new();
    private readonly ConcurrentDictionary<Guid, bool> _inGracePeriodState = new();
    private readonly ConcurrentDictionary<Guid, bool> _exhaustedState = new();
    private readonly object _stateLock = new();

    private DateTime? _lastKnownDayStartUtc;
    private CancellationTokenSource? _cts;
    private Task? _evaluationTask;
    private int _isDisposed;

    public event EventHandler<LimitWarningEventArgs>? LimitWarningIssued;
    public event EventHandler<GracePeriodEventArgs>? GracePeriodStarted;
    public event EventHandler<GracePeriodEventArgs>? GracePeriodUpdated;
    public event EventHandler<QuotaExhaustedEventArgs>? QuotaExhausted;
    public event EventHandler<DayRolloverEventArgs>? DayRolloverOccurred;

    public bool IsRunning => _evaluationTask != null && !_evaluationTask.IsCompleted;

    public LimitManagerService(
        IGameProfileRepository gameRepository,
        ILimitRepository limitRepository,
        ISessionRepository sessionRepository,
        ITimeTrackerService timeTracker,
        ILimitCalculator limitCalculator,
        IDayBoundaryCalculator boundaryCalculator,
        ISystemClock systemClock,
        ILogger<LimitManagerService> logger,
        TimeZoneInfo? timeZone = null,
        IEnumerable<int>? warningThresholdsMinutes = null,
        TimeSpan? evaluationInterval = null)
    {
        _gameRepository = gameRepository;
        _limitRepository = limitRepository;
        _sessionRepository = sessionRepository;
        _timeTracker = timeTracker;
        _limitCalculator = limitCalculator;
        _boundaryCalculator = boundaryCalculator;
        _systemClock = systemClock;
        _logger = logger;

        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _warningThresholdsMinutes = (warningThresholdsMinutes ?? new[] { 15, 5, 1 })
            .OrderByDescending(m => m)
            .ToList();
        _evaluationInterval = evaluationInterval ?? TimeSpan.FromSeconds(2);

        _timeTracker.SessionStarted += OnSessionActivity;
        _timeTracker.SessionUpdated += OnSessionActivity;
        _timeTracker.SessionEnded += OnSessionActivity;
    }

    public async Task<LimitCalculationResult> GetCurrentStatusAsync(Guid gameProfileId, CancellationToken cancellationToken = default)
    {
        var game = await _gameRepository.GetByIdAsync(gameProfileId, cancellationToken).ConfigureAwait(false);
        if (game == null)
        {
            throw new ArgumentException($"GameProfile with ID {gameProfileId} not found.", nameof(gameProfileId));
        }

        var individualLimit = await _limitRepository.GetIndividualLimitAsync(gameProfileId, cancellationToken).ConfigureAwait(false);
        var sharedLimit = await _limitRepository.GetSharedLimitAsync(cancellationToken).ConfigureAwait(false);

        int resetHour = individualLimit?.ResetHourUtc ?? sharedLimit?.ResetHourUtc ?? 0;
        var boundary = _boundaryCalculator.GetDayBoundary(_systemClock.UtcNow, _timeZone, resetHour);

        int dbIndSeconds = await _sessionRepository.GetTotalSecondsUsedForGameTodayAsync(
            gameProfileId,
            boundary.StartUtc,
            boundary.EndUtc,
            cancellationToken).ConfigureAwait(false);

        int dbSharedSeconds = await _sessionRepository.GetTotalSharedSecondsUsedTodayAsync(
            boundary.StartUtc,
            boundary.EndUtc,
            cancellationToken).ConfigureAwait(false);

        var activeSessions = _timeTracker.GetActiveSessions();
        var currentActiveSession = activeSessions.FirstOrDefault(s => s.GameProfileId == gameProfileId);
        int currentActiveSeconds = currentActiveSession?.DurationSeconds ?? 0;

        int otherActiveSeconds = activeSessions
            .Where(s => s.GameProfileId != gameProfileId)
            .Sum(s => s.DurationSeconds);

        int totalSharedSeconds = dbSharedSeconds + otherActiveSeconds;

        return _limitCalculator.Calculate(
            game.Id,
            game.Name,
            individualLimit,
            sharedLimit,
            dbIndSeconds,
            totalSharedSeconds,
            currentActiveSeconds);
    }

    public async Task<IReadOnlyList<LimitCalculationResult>> GetAllCurrentStatusesAsync(CancellationToken cancellationToken = default)
    {
        var games = await _gameRepository.GetMonitoredAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<LimitCalculationResult>(games.Count);

        foreach (var game in games)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var status = await GetCurrentStatusAsync(game.Id, cancellationToken).ConfigureAwait(false);
            list.Add(status);
        }

        return list;
    }

    public async Task CheckLimitsAsync(CancellationToken cancellationToken = default)
    {
        DateTime nowUtc = _systemClock.UtcNow;
        CheckDayRollover(nowUtc);

        var monitoredGames = await _gameRepository.GetMonitoredAsync(cancellationToken).ConfigureAwait(false);
        var activeSessions = _timeTracker.GetActiveSessions();

        foreach (var game in monitoredGames)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var status = await GetCurrentStatusAsync(game.Id, cancellationToken).ConfigureAwait(false);
            if (!status.HasLimit)
            {
                continue;
            }

            bool isGameActive = activeSessions.Any(s => s.GameProfileId == game.Id);

            EvaluateWarnings(game, status);
            EvaluateGracePeriodAndExhaustion(game, status, isGameActive);
        }
    }

    private void CheckDayRollover(DateTime nowUtc)
    {
        // Compute standard midnight boundary
        var currentBoundary = _boundaryCalculator.GetDayBoundary(nowUtc, _timeZone, 0);

        lock (_stateLock)
        {
            if (_lastKnownDayStartUtc == null)
            {
                _lastKnownDayStartUtc = currentBoundary.StartUtc;
                return;
            }

            if (currentBoundary.StartUtc != _lastKnownDayStartUtc.Value)
            {
                var previousStart = _lastKnownDayStartUtc.Value;
                _lastKnownDayStartUtc = currentBoundary.StartUtc;

                _firedWarnings.Clear();
                _inGracePeriodState.Clear();
                _exhaustedState.Clear();

                _logger.LogInformation("Day rollover detected. Resetting all limit warnings and usage counters.");

                SafeRaiseEvent(DayRolloverOccurred, new DayRolloverEventArgs
                {
                    PreviousDayStartUtc = previousStart,
                    NewDayStartUtc = currentBoundary.StartUtc,
                    TimestampUtc = nowUtc
                });
            }
        }
    }

    private void EvaluateWarnings(GameProfile game, LimitCalculationResult status)
    {
        if (status.IsLimitExceeded || !status.MinutesRemaining.HasValue)
        {
            return;
        }

        int remainingMinutes = status.MinutesRemaining.Value;
        var gameWarnings = _firedWarnings.GetOrAdd(game.Id, _ => new HashSet<int>());

        foreach (int threshold in _warningThresholdsMinutes)
        {
            if (remainingMinutes <= threshold)
            {
                bool newlyFired = false;
                lock (_stateLock)
                {
                    if (gameWarnings.Add(threshold))
                    {
                        newlyFired = true;
                    }
                }

                if (newlyFired)
                {
                    _logger.LogInformation(
                        "Limit warning threshold reached for {GameName}: {Remaining}m remaining (Threshold: {Threshold}m).",
                        game.Name,
                        remainingMinutes,
                        threshold);

                    SafeRaiseEvent(LimitWarningIssued, new LimitWarningEventArgs
                    {
                        Game = game,
                        CalculationResult = status,
                        WarningThresholdMinutes = threshold,
                        TimestampUtc = _systemClock.UtcNow
                    });
                }
            }
        }
    }

    private void EvaluateGracePeriodAndExhaustion(GameProfile game, LimitCalculationResult status, bool isGameActive)
    {
        if (status.IsInGracePeriod)
        {
            bool isFirstGraceNotification = false;
            lock (_stateLock)
            {
                if (!_inGracePeriodState.TryGetValue(game.Id, out bool inGrace) || !inGrace)
                {
                    _inGracePeriodState[game.Id] = true;
                    isFirstGraceNotification = true;
                }
            }

            var graceArgs = new GracePeriodEventArgs
            {
                Game = game,
                CalculationResult = status,
                GracePeriodSecondsRemaining = status.GracePeriodSecondsRemaining,
                TimestampUtc = _systemClock.UtcNow
            };

            if (isFirstGraceNotification)
            {
                _logger.LogWarning("Grace period started for {GameName}. Remaining grace: {GraceSeconds}s.", game.Name, status.GracePeriodSecondsRemaining);
                SafeRaiseEvent(GracePeriodStarted, graceArgs);
            }

            SafeRaiseEvent(GracePeriodUpdated, graceArgs);
        }
        else if (status.IsLimitExceeded)
        {
            // Limit exceeded and grace period has run out!
            bool isFirstExhaustion = false;
            lock (_stateLock)
            {
                if (!_exhaustedState.TryGetValue(game.Id, out bool exhausted) || !exhausted)
                {
                    _exhaustedState[game.Id] = true;
                    _inGracePeriodState[game.Id] = false;
                    isFirstExhaustion = true;
                }
            }

            if (isFirstExhaustion)
            {
                _logger.LogWarning("Daily allowance and grace period fully exhausted for {GameName}.", game.Name);
                SafeRaiseEvent(QuotaExhausted, new QuotaExhaustedEventArgs
                {
                    Game = game,
                    CalculationResult = status,
                    TimestampUtc = _systemClock.UtcNow
                });
            }
        }
        else
        {
            // Normal operating condition (within quota)
            lock (_stateLock)
            {
                _inGracePeriodState[game.Id] = false;
                _exhaustedState[game.Id] = false;
            }
        }
    }

    private void OnSessionActivity(object? sender, SessionLifecycleEventArgs args)
    {
        _ = CheckLimitsAsync();
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _cts = new CancellationTokenSource();
        _evaluationTask = Task.Run(() => EvaluationLoopAsync(_cts.Token), CancellationToken.None);
        _logger.LogInformation("LimitManagerService started with evaluation interval: {Interval} ms.", _evaluationInterval.TotalMilliseconds);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _cts == null)
        {
            return;
        }

        _logger.LogInformation("Stopping LimitManagerService...");
        _cts.Cancel();

        if (_evaluationTask != null)
        {
            try
            {
                await _evaluationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while stopping LimitManagerService evaluation loop.");
            }
            finally
            {
                _evaluationTask = null;
            }
        }

        _cts.Dispose();
        _cts = null;
        _logger.LogInformation("LimitManagerService stopped.");
    }

    private async Task EvaluationLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckLimitsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in LimitManagerService cycle.");
            }

            try
            {
                await Task.Delay(_evaluationInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
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
                _logger.LogError(ex, "Error occurred in LimitManagerService event handler for {EventType}.", typeof(T).Name);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _timeTracker.SessionStarted -= OnSessionActivity;
        _timeTracker.SessionUpdated -= OnSessionActivity;
        _timeTracker.SessionEnded -= OnSessionActivity;

        _cts?.Cancel();
        _cts?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        Dispose();
    }
}

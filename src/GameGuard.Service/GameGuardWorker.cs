using System.Text.Json;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Ipc;
using GameGuard.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameGuard.Service;

public sealed class GameGuardWorker : BackgroundService
{
    private readonly IDatabaseInitializer _databaseInitializer;
    private readonly ITimeTrackerService _timeTrackerService;
    private readonly IProcessMonitor _processMonitor;
    private readonly ILimitManagerService _limitManagerService;
    private readonly IEnforcementService _enforcementService;
    private readonly IRelaunchSuppressionService _relaunchSuppressionService;
    private readonly IAdminOverrideRepository _overrideRepository;
    private readonly IGameProfileRepository _profileRepository;
    private readonly IClockTamperDetector _clockTamperDetector;
    private readonly IIpcServer _ipcServer;
    private readonly ISystemClock _clock;
    private readonly ILogger<GameGuardWorker> _logger;

    public GameGuardWorker(
        IDatabaseInitializer databaseInitializer,
        ITimeTrackerService timeTrackerService,
        IProcessMonitor processMonitor,
        ILimitManagerService limitManagerService,
        IEnforcementService enforcementService,
        IRelaunchSuppressionService relaunchSuppressionService,
        IAdminOverrideRepository overrideRepository,
        IGameProfileRepository profileRepository,
        IClockTamperDetector clockTamperDetector,
        IIpcServer ipcServer,
        ISystemClock clock,
        ILogger<GameGuardWorker> logger)
    {
        _databaseInitializer = databaseInitializer ?? throw new ArgumentNullException(nameof(databaseInitializer));
        _timeTrackerService = timeTrackerService ?? throw new ArgumentNullException(nameof(timeTrackerService));
        _processMonitor = processMonitor ?? throw new ArgumentNullException(nameof(processMonitor));
        _limitManagerService = limitManagerService ?? throw new ArgumentNullException(nameof(limitManagerService));
        _enforcementService = enforcementService ?? throw new ArgumentNullException(nameof(enforcementService));
        _relaunchSuppressionService = relaunchSuppressionService ?? throw new ArgumentNullException(nameof(relaunchSuppressionService));
        _overrideRepository = overrideRepository ?? throw new ArgumentNullException(nameof(overrideRepository));
        _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
        _clockTamperDetector = clockTamperDetector ?? throw new ArgumentNullException(nameof(clockTamperDetector));
        _ipcServer = ipcServer ?? throw new ArgumentNullException(nameof(ipcServer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        RegisterIpcHandlers();
        SubscribeDomainEvents();
    }

    private void RegisterIpcHandlers()
    {
        _ipcServer.RegisterHandler(IpcActions.GetStatus, HandleGetStatusAsync);
        _ipcServer.RegisterHandler(IpcActions.GrantBonus, HandleGrantBonusAsync);
        _ipcServer.RegisterHandler(IpcActions.EmergencyLock, HandleEmergencyLockAsync);
        _ipcServer.RegisterHandler(IpcActions.EndSession, HandleEndSessionAsync);
    }

    private void SubscribeDomainEvents()
    {
        _limitManagerService.LimitWarningIssued += async (_, e) =>
        {
            var remaining = e.CalculationResult.SecondsRemaining.HasValue
                ? TimeSpan.FromSeconds(e.CalculationResult.SecondsRemaining.Value)
                : TimeSpan.FromMinutes(e.WarningThresholdMinutes);

            string remainingStr = remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m"
                : $"{remaining.Minutes}m";

            var payload = new NotificationEventPayload(
                "GameGuard Limit Warning",
                $"{e.Game.Name}: You have {remainingStr} remaining today.",
                (int)NotificationType.Warning);

            await _ipcServer.BroadcastEventAsync(IpcActions.NotificationEvent, payload).ConfigureAwait(false);
        };

        _limitManagerService.GracePeriodStarted += async (_, e) =>
        {
            var payload = new NotificationEventPayload(
                "GameGuard: Time's Up",
                $"{e.Game.Name}: Daily limit reached. You have {e.GracePeriodSecondsRemaining}s grace period to save and exit.",
                (int)NotificationType.Warning);

            await _ipcServer.BroadcastEventAsync(IpcActions.NotificationEvent, payload).ConfigureAwait(false);
        };

        _enforcementService.GameEnforced += async (_, e) =>
        {
            if (e.Result.Success)
            {
                var payload = new NotificationEventPayload(
                    "GameGuard Enforcement",
                    $"{e.Result.GameName}: Session closed as daily playtime limit expired.",
                    (int)NotificationType.Danger);

                await _ipcServer.BroadcastEventAsync(IpcActions.NotificationEvent, payload).ConfigureAwait(false);
            }
        };

        _relaunchSuppressionService.RelaunchBlocked += async (_, e) =>
        {
            var payload = new NotificationEventPayload(
                "GameGuard: Launch Blocked",
                $"{e.ProcessName} blocked: {e.Reason}",
                (int)NotificationType.Danger);

            await _ipcServer.BroadcastEventAsync(IpcActions.NotificationEvent, payload).ConfigureAwait(false);
        };

        _timeTrackerService.SessionStarted += async (_, _) =>
        {
            await _ipcServer.BroadcastEventAsync(IpcActions.SessionUpdatedEvent, null).ConfigureAwait(false);
        };

        _timeTrackerService.SessionEnded += async (_, _) =>
        {
            await _ipcServer.BroadcastEventAsync(IpcActions.SessionUpdatedEvent, null).ConfigureAwait(false);
        };
    }

    private Task<IpcMessage> HandleGetStatusAsync(IpcMessage request)
    {
        var activeSessions = _timeTrackerService.GetActiveSessions();
        var sessionInfos = activeSessions.Select(s => new ActiveSessionInfo(
            s.Id,
            s.GameProfileId,
            s.ProcessName,
            s.ProcessId,
            s.ProcessName,
            s.DurationSeconds)).ToList();

        var responseData = new ServiceStatusResponse(
            IsMonitoringActive: _processMonitor.IsRunning,
            ActiveSessionsCount: activeSessions.Count,
            ActiveSessions: sessionInfos);

        string json = JsonSerializer.Serialize(responseData);
        return Task.FromResult(IpcMessage.CreateResponse(request.Id, request.Action, json));
    }

    private async Task<IpcMessage> HandleGrantBonusAsync(IpcMessage request)
    {
        if (string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            var err = JsonSerializer.Serialize(new CommandResultResponse(false, "Invalid payload."));
            return IpcMessage.CreateResponse(request.Id, request.Action, err);
        }

        var req = JsonSerializer.Deserialize<GrantBonusRequest>(request.PayloadJson);
        if (req == null)
        {
            var err = JsonSerializer.Serialize(new CommandResultResponse(false, "Invalid bonus request payload."));
            return IpcMessage.CreateResponse(request.Id, request.Action, err);
        }

        var bonus = AdminOverride.CreateBonusTime(
            req.GameProfileId,
            req.BonusMinutes,
            _clock.UtcNow,
            TimeSpan.FromHours(4),
            req.Reason ?? "Granted via IPC");

        await _overrideRepository.AddOverrideAsync(bonus).ConfigureAwait(false);
        _logger.LogInformation("Granted {Minutes} bonus minutes via IPC. Target game: {GameId}.", req.BonusMinutes, req.GameProfileId);

        var result = JsonSerializer.Serialize(new CommandResultResponse(true));
        return IpcMessage.CreateResponse(request.Id, request.Action, result);
    }

    private async Task<IpcMessage> HandleEmergencyLockAsync(IpcMessage request)
    {
        string? reason = "Emergency lock triggered via IPC";
        Guid? gameId = null;

        if (!string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            var req = JsonSerializer.Deserialize<EmergencyLockRequest>(request.PayloadJson);
            if (req != null)
            {
                reason = req.Reason ?? reason;
                gameId = req.GameProfileId;
            }
        }

        var lockOverride = AdminOverride.CreateEmergencyLock(
            gameId,
            _clock.UtcNow,
            TimeSpan.FromHours(24),
            reason);

        await _overrideRepository.AddOverrideAsync(lockOverride).ConfigureAwait(false);
        _logger.LogWarning("Emergency lockdown applied via IPC. GameId: {GameId}.", gameId);

        // Terminate any active sessions immediately
        var activeSessions = _timeTrackerService.GetActiveSessions();
        var games = await _profileRepository.GetAllAsync().ConfigureAwait(false);
        foreach (var session in activeSessions)
        {
            if (!gameId.HasValue || session.GameProfileId == gameId.Value)
            {
                var profile = games.FirstOrDefault(g => g.Id == session.GameProfileId);
                if (profile != null)
                {
                    await _enforcementService.EnforceGameAsync(
                        profile,
                        session.ProcessId,
                        session.ProcessName,
                        TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
                }
            }
        }

        var result = JsonSerializer.Serialize(new CommandResultResponse(true));
        return IpcMessage.CreateResponse(request.Id, request.Action, result);
    }

    private async Task<IpcMessage> HandleEndSessionAsync(IpcMessage request)
    {
        if (string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            var err = JsonSerializer.Serialize(new CommandResultResponse(false, "Invalid payload."));
            return IpcMessage.CreateResponse(request.Id, request.Action, err);
        }

        var req = JsonSerializer.Deserialize<EndSessionRequest>(request.PayloadJson);
        if (req == null)
        {
            var err = JsonSerializer.Serialize(new CommandResultResponse(false, "Invalid end session payload."));
            return IpcMessage.CreateResponse(request.Id, request.Action, err);
        }

        var session = _timeTrackerService.GetActiveSession(req.ProcessId);
        if (session == null)
        {
            var err = JsonSerializer.Serialize(new CommandResultResponse(false, $"No active session found for PID {req.ProcessId}."));
            return IpcMessage.CreateResponse(request.Id, request.Action, err);
        }

        var games = await _profileRepository.GetAllAsync().ConfigureAwait(false);
        var profile = games.FirstOrDefault(g => g.Id == session.GameProfileId);
        if (profile == null)
        {
            var err = JsonSerializer.Serialize(new CommandResultResponse(false, $"Game profile {session.GameProfileId} not found."));
            return IpcMessage.CreateResponse(request.Id, request.Action, err);
        }

        var enforcementResult = await _enforcementService.EnforceGameAsync(
            profile,
            session.ProcessId,
            session.ProcessName,
            TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        _logger.LogInformation("End session requested via IPC for PID {Pid}. Success: {Success}.", req.ProcessId, enforcementResult.Success);
        var res = JsonSerializer.Serialize(new CommandResultResponse(enforcementResult.Success, enforcementResult.ErrorMessage));
        return IpcMessage.CreateResponse(request.Id, request.Action, res);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GameGuard Background Service initializing...");

        try
        {
            // 1. Initialize SQLite persistence schema and default seeds
            await _databaseInitializer.InitializeAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("SQLite persistence schema initialized.");

            // 2. Initialize active tracking state and recover orphaned sessions
            await _timeTrackerService.InitializeAsync(stoppingToken).ConfigureAwait(false);
            await _timeTrackerService.ReconcileOrphanedSessionsAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("Time tracking engine initialized and orphaned sessions reconciled.");

            // 3. Start background monitoring and enforcement engines
            await _processMonitor.StartAsync(stoppingToken).ConfigureAwait(false);
            await _limitManagerService.StartAsync(stoppingToken).ConfigureAwait(false);
            await _enforcementService.StartAsync(stoppingToken).ConfigureAwait(false);
            await _relaunchSuppressionService.StartAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("All background monitoring, limit evaluation, and enforcement engines started.");

            // 4. Start Named Pipe IPC Server
            await _ipcServer.StartAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("Named Pipe IPC Server active for UI communication.");

            // 5. Heartbeat and anti-tamper watchdog loop
            int heartbeatTicks = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);

                    // Offline system clock rollback anti-tamper verification
                    _clockTamperDetector.ValidateSystemTime(_clock.UtcNow);

                    heartbeatTicks++;
                    if (heartbeatTicks % 6 == 0) // Log summary every 60 seconds
                    {
                        var activeSessions = _timeTrackerService.GetActiveSessions();
                        if (activeSessions.Count > 0)
                        {
                            var activeGameNames = string.Join(", ", activeSessions.Select(s => s.ProcessName));
                            _logger.LogInformation("GameGuard Active: {Count} active gaming session(s) [{Games}]. Connected IPC clients: {Clients}.",
                                activeSessions.Count, activeGameNames, _ipcServer.ConnectedClientCount);
                        }
                        else
                        {
                            _logger.LogDebug("GameGuard Idle: No monitored games running. Protection active. Connected IPC clients: {Clients}.",
                                _ipcServer.ConnectedClientCount);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during GameGuard heartbeat watchdog loop; continuing.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("GameGuard Background Service stop requested.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error occurred while running GameGuard Background Service.");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GameGuard Background Service shutting down...");

        try
        {
            await _ipcServer.StopAsync(cancellationToken).ConfigureAwait(false);
            await _relaunchSuppressionService.StopAsync(cancellationToken).ConfigureAwait(false);
            await _enforcementService.StopAsync(cancellationToken).ConfigureAwait(false);
            await _limitManagerService.StopAsync(cancellationToken).ConfigureAwait(false);
            await _processMonitor.StopAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("All GameGuard background engines stopped cleanly.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception encountered during GameGuard background engine shutdown.");
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}

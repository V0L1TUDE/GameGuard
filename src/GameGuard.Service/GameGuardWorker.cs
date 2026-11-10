using GameGuard.Core.Interfaces;
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
    private readonly IClockTamperDetector _clockTamperDetector;
    private readonly ISystemClock _clock;
    private readonly ILogger<GameGuardWorker> _logger;

    public GameGuardWorker(
        IDatabaseInitializer databaseInitializer,
        ITimeTrackerService timeTrackerService,
        IProcessMonitor processMonitor,
        ILimitManagerService limitManagerService,
        IEnforcementService enforcementService,
        IRelaunchSuppressionService relaunchSuppressionService,
        IClockTamperDetector clockTamperDetector,
        ISystemClock clock,
        ILogger<GameGuardWorker> logger)
    {
        _databaseInitializer = databaseInitializer ?? throw new ArgumentNullException(nameof(databaseInitializer));
        _timeTrackerService = timeTrackerService ?? throw new ArgumentNullException(nameof(timeTrackerService));
        _processMonitor = processMonitor ?? throw new ArgumentNullException(nameof(processMonitor));
        _limitManagerService = limitManagerService ?? throw new ArgumentNullException(nameof(limitManagerService));
        _enforcementService = enforcementService ?? throw new ArgumentNullException(nameof(enforcementService));
        _relaunchSuppressionService = relaunchSuppressionService ?? throw new ArgumentNullException(nameof(relaunchSuppressionService));
        _clockTamperDetector = clockTamperDetector ?? throw new ArgumentNullException(nameof(clockTamperDetector));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            // 4. Heartbeat and anti-tamper watchdog loop
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
                            _logger.LogInformation("GameGuard Active: {Count} active gaming session(s) [{Games}].", activeSessions.Count, activeGameNames);
                        }
                        else
                        {
                            _logger.LogDebug("GameGuard Idle: No monitored games running. Protection active.");
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

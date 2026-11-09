using GameGuard.Core.Interfaces;
using GameGuard.Core.Services;
using GameGuard.Infrastructure.Enforcement;
using GameGuard.Infrastructure.Limits;
using GameGuard.Infrastructure.Monitoring;
using GameGuard.Infrastructure.Native;
using GameGuard.Infrastructure.Persistence;
using GameGuard.Infrastructure.Security;
using GameGuard.Infrastructure.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameGuard.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGameGuardCore(this IServiceCollection services)
    {
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<IMonotonicClock, MonotonicClock>();
        services.AddSingleton<ILimitCalculator, LimitCalculator>();
        services.AddSingleton<IDayBoundaryCalculator, DayBoundaryCalculator>();
        services.AddSingleton<IClockTamperDetector, ClockTamperDetector>();
        return services;
    }

    public static IServiceCollection AddGameGuardSqlite(this IServiceCollection services, string databasePath)
    {
        services.AddGameGuardCore();

        services.AddSingleton(new SqliteConnectionFactory(databasePath));
        services.AddSingleton<IDatabaseInitializer, SqliteDatabaseInitializer>();
        services.AddSingleton<IGameProfileRepository, SqliteGameProfileRepository>();
        services.AddSingleton<ILimitRepository, SqliteLimitRepository>();
        services.AddSingleton<ISessionRepository, SqliteSessionRepository>();
        services.AddSingleton<IAdminOverrideRepository, SqliteAdminOverrideRepository>();
        services.AddSingleton<IRelaunchBlockRepository, SqliteRelaunchBlockRepository>();
        services.AddSingleton<IAdminAuthService, SqliteAdminAuthService>();

        return services;
    }

    public static IServiceCollection AddGameGuardProcessMonitoring(this IServiceCollection services, TimeSpan? pollingInterval = null)
    {
        services.AddSingleton<IProcessScanner, Win32ProcessScanner>();
        services.AddSingleton<IProcessMonitor>(sp => new ProcessMonitor(
            sp.GetRequiredService<IProcessScanner>(),
            sp.GetRequiredService<IGameProfileRepository>(),
            sp.GetRequiredService<ISystemClock>(),
            sp.GetRequiredService<ILogger<ProcessMonitor>>(),
            pollingInterval));

        return services;
    }

    public static IServiceCollection AddGameGuardTimeTracking(
        this IServiceCollection services,
        TimeSpan? checkpointInterval = null,
        TimeSpan? suspensionGapThreshold = null)
    {
        services.AddSingleton<ITimeTrackerService>(sp => new TimeTrackerService(
            sp.GetRequiredService<IProcessMonitor>(),
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<IGameProfileRepository>(),
            sp.GetRequiredService<IProcessScanner>(),
            sp.GetRequiredService<ISystemClock>(),
            sp.GetRequiredService<IMonotonicClock>(),
            sp.GetRequiredService<ILogger<TimeTrackerService>>(),
            checkpointInterval,
            suspensionGapThreshold));

        return services;
    }

    public static IServiceCollection AddGameGuardLimitManagement(
        this IServiceCollection services,
        TimeZoneInfo? timeZone = null,
        IEnumerable<int>? warningThresholdsMinutes = null,
        TimeSpan? evaluationInterval = null)
    {
        services.AddSingleton<ILimitManagerService>(sp => new LimitManagerService(
            sp.GetRequiredService<IGameProfileRepository>(),
            sp.GetRequiredService<ILimitRepository>(),
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<ITimeTrackerService>(),
            sp.GetRequiredService<ILimitCalculator>(),
            sp.GetRequiredService<IDayBoundaryCalculator>(),
            sp.GetRequiredService<ISystemClock>(),
            sp.GetRequiredService<ILogger<LimitManagerService>>(),
            timeZone,
            warningThresholdsMinutes,
            evaluationInterval,
            sp.GetService<IAdminOverrideRepository>(),
            sp.GetService<IClockTamperDetector>()));

        return services;
    }

    public static IServiceCollection AddGameGuardEnforcement(this IServiceCollection services)
    {
        services.AddSingleton<IProcessTerminator, Win32ProcessTerminator>();
        services.AddSingleton<IEnforcementService>(sp => new EnforcementService(
            sp.GetRequiredService<ILimitManagerService>(),
            sp.GetRequiredService<ITimeTrackerService>(),
            sp.GetRequiredService<IGameProfileRepository>(),
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<IProcessTerminator>(),
            sp.GetRequiredService<IProcessScanner>(),
            sp.GetRequiredService<ISystemClock>(),
            sp.GetRequiredService<ILogger<EnforcementService>>()));

        return services;
    }

    public static IServiceCollection AddGameGuardRelaunchSuppression(this IServiceCollection services)
    {
        services.AddSingleton<IRelaunchSuppressionService>(sp => new RelaunchSuppressionService(
            sp.GetRequiredService<IProcessMonitor>(),
            sp.GetRequiredService<ILimitManagerService>(),
            sp.GetRequiredService<IProcessTerminator>(),
            sp.GetRequiredService<IRelaunchBlockRepository>(),
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<ITimeTrackerService>(),
            sp.GetRequiredService<ISystemClock>(),
            sp.GetRequiredService<ILogger<RelaunchSuppressionService>>()));

        return services;
    }
}

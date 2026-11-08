using GameGuard.Core.Interfaces;
using GameGuard.Core.Services;
using GameGuard.Infrastructure.Monitoring;
using GameGuard.Infrastructure.Native;
using GameGuard.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameGuard.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGameGuardCore(this IServiceCollection services)
    {
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<ILimitCalculator, LimitCalculator>();
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
}

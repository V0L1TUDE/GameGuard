using GameGuard.Core.Interfaces;
using GameGuard.Core.Services;
using GameGuard.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

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
}

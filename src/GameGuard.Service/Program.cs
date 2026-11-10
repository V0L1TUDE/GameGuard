using GameGuard.Infrastructure.DependencyInjection;
using GameGuard.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameGuard.Service;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0)
        {
            string command = args[0].ToLowerInvariant();
            switch (command)
            {
                case "--install":
                case "-i":
                    return await ServiceManager.InstallServiceAsync();

                case "--uninstall":
                case "-u":
                    return await ServiceManager.UninstallServiceAsync();

                case "--start":
                    return await ServiceManager.StartServiceAsync();

                case "--stop":
                    return await ServiceManager.StopServiceAsync();

                case "--status":
                case "-s":
                    ServiceManager.PrintStatus();
                    return 0;

                case "--help":
                case "-h":
                case "/?":
                    PrintHelp();
                    return 0;

                default:
                    Console.WriteLine($"Unknown command: {args[0]}");
                    PrintHelp();
                    return 1;
            }
        }

        // Run as Windows Service or interactive console worker
        var builder = Host.CreateApplicationBuilder(args);

        // Enable Windows Service integration
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = ServiceManager.ServiceName;
        });

        // Determine shared database path
        string dbPath = GameGuardPaths.GetDatabasePath(preferSharedProgramData: true);

        // Configure infrastructure services
        builder.Services.AddGameGuardSqlite(dbPath);
        builder.Services.AddGameGuardProcessMonitoring();
        builder.Services.AddGameGuardTimeTracking();
        builder.Services.AddGameGuardLimitManagement();
        builder.Services.AddGameGuardEnforcement();
        builder.Services.AddGameGuardRelaunchSuppression();
        builder.Services.AddGameGuardAnalytics();
        builder.Services.AddGameGuardNotifications();
        builder.Services.AddGameGuardIpcServer();

        // Register the background worker daemon
        builder.Services.AddHostedService<GameGuardWorker>();

        var host = builder.Build();
        await host.RunAsync();
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("GameGuard Background Service");
        Console.WriteLine("Usage: GameGuard.Service [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --install, -i      Install as a Windows Service (runs on startup)");
        Console.WriteLine("  --uninstall, -u    Stop and remove the Windows Service");
        Console.WriteLine("  --start            Start the installed Windows Service");
        Console.WriteLine("  --stop             Stop the running Windows Service");
        Console.WriteLine("  --status, -s       Display current service status");
        Console.WriteLine("  --help, -h         Show help and usage information");
        Console.WriteLine();
        Console.WriteLine("Running without arguments executes the background worker in interactive/service mode.");
    }
}

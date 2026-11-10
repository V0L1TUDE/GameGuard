using System.IO;
using System.Windows;
using GameGuard.Core.Interfaces;
using GameGuard.Infrastructure.DependencyInjection;
using GameGuard.Infrastructure.Notifications;
using GameGuard.UI.Mvvm;
using GameGuard.UI.Services;
using GameGuard.UI.Services.Tray;
using GameGuard.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameGuard.UI;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);

        _serviceProvider = services.BuildServiceProvider();

        // Initialize SQLite Database schema & initial seeds
        var dbInitializer = _serviceProvider.GetRequiredService<IDatabaseInitializer>();
        await dbInitializer.InitializeAsync();

        // Start background monitoring & enforcement engines
        await _serviceProvider.GetRequiredService<ITimeTrackerService>().InitializeAsync();
        await _serviceProvider.GetRequiredService<IProcessMonitor>().StartAsync();
        await _serviceProvider.GetRequiredService<ILimitManagerService>().StartAsync();
        await _serviceProvider.GetRequiredService<IEnforcementService>().StartAsync();
        await _serviceProvider.GetRequiredService<IRelaunchSuppressionService>().StartAsync();

        // Start notification dispatcher service
        _serviceProvider.GetRequiredService<NotificationDispatcherService>();

        // Launch Shell Window
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();

        // Initialize System Tray Manager
        var trayManager = _serviceProvider.GetRequiredService<ISystemTrayManager>();
        trayManager.Initialize(mainWindow);

        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
        });

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dbDirectory = Path.Combine(appData, "GameGuard");
        Directory.CreateDirectory(dbDirectory);
        string dbPath = Path.Combine(dbDirectory, "gameguard.db");

        // Infrastructure Services
        services.AddGameGuardSqlite(dbPath);
        services.AddGameGuardProcessMonitoring();
        services.AddGameGuardTimeTracking();
        services.AddGameGuardLimitManagement();
        services.AddGameGuardEnforcement();
        services.AddGameGuardRelaunchSuppression();
        services.AddGameGuardAnalytics();
        services.AddGameGuardNotifications();

        // MVVM & UI Services
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ISystemTrayManager, SystemTrayManager>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<GamesViewModel>();
        services.AddTransient<LimitsViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<MainWindow>();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider != null)
        {
            try
            {
                _serviceProvider.GetService<ISystemTrayManager>()?.Dispose();
                _serviceProvider.GetService<NotificationDispatcherService>()?.Dispose();

                await _serviceProvider.GetRequiredService<IRelaunchSuppressionService>().StopAsync();
                await _serviceProvider.GetRequiredService<IEnforcementService>().StopAsync();
                await _serviceProvider.GetRequiredService<ILimitManagerService>().StopAsync();
                await _serviceProvider.GetRequiredService<IProcessMonitor>().StopAsync();
            }
            catch
            {
                // Graceful best-effort shutdown
            }
            finally
            {
                _serviceProvider.Dispose();
            }
        }

        base.OnExit(e);
    }
}

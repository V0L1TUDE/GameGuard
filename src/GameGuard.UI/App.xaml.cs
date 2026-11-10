using System.IO;
using System.Windows;
using GameGuard.Core.Interfaces;
using GameGuard.Infrastructure.DependencyInjection;
using GameGuard.Infrastructure.Notifications;
using GameGuard.Infrastructure.Persistence;
using GameGuard.Infrastructure.Services;
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
    private bool _isServiceActive;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);

        _serviceProvider = services.BuildServiceProvider();

        // Initialize SQLite Database schema & initial seeds
        var dbInitializer = _serviceProvider.GetRequiredService<IDatabaseInitializer>();
        await dbInitializer.InitializeAsync();

        // Check if Windows Background Service is already running
        _isServiceActive = WindowsServiceDetector.IsServiceRunning();

        // Initialize active tracking state
        await _serviceProvider.GetRequiredService<ITimeTrackerService>().InitializeAsync();

        // If background service is NOT running, run background monitoring engines in-process as fallback
        if (!_isServiceActive)
        {
            await _serviceProvider.GetRequiredService<IProcessMonitor>().StartAsync();
            await _serviceProvider.GetRequiredService<ILimitManagerService>().StartAsync();
            await _serviceProvider.GetRequiredService<IEnforcementService>().StartAsync();
            await _serviceProvider.GetRequiredService<IRelaunchSuppressionService>().StartAsync();
        }

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

        string dbPath = GameGuardPaths.GetDatabasePath(preferSharedProgramData: true);

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

                if (!_isServiceActive)
                {
                    await _serviceProvider.GetRequiredService<IRelaunchSuppressionService>().StopAsync();
                    await _serviceProvider.GetRequiredService<IEnforcementService>().StopAsync();
                    await _serviceProvider.GetRequiredService<ILimitManagerService>().StopAsync();
                    await _serviceProvider.GetRequiredService<IProcessMonitor>().StopAsync();
                }
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

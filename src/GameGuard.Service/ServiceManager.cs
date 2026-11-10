using System.Diagnostics;
using System.ServiceProcess;

namespace GameGuard.Service;

public static class ServiceManager
{
    public const string ServiceName = "GameGuard";
    public const string DisplayName = "GameGuard Background Service";
    public const string Description = "Offline gaming time defense and limit enforcement service.";

    public static bool IsServiceInstalled()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            _ = sc.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static ServiceControllerStatus? GetServiceStatus()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            return sc.Status;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<int> InstallServiceAsync()
    {
        Console.WriteLine($"Installing Windows Service '{ServiceName}'...");

        string exePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine executable path.");

        // 1. Create service with auto startup
        int exitCode = RunScCommand($"create \"{ServiceName}\" binPath= \"\\\"{exePath}\\\"\" start= auto DisplayName= \"{DisplayName}\"");
        if (exitCode != 0)
        {
            Console.Error.WriteLine($"Failed to create Windows service. Error code: {exitCode}. Ensure running as Administrator.");
            return exitCode;
        }

        // 2. Set description
        RunScCommand($"description \"{ServiceName}\" \"{Description}\"");

        // 3. Configure recovery on failure (restart service after 60 seconds)
        RunScCommand($"failure \"{ServiceName}\" reset= 86400 actions= restart/60000/restart/60000/restart/60000");

        Console.WriteLine($"Windows Service '{ServiceName}' installed successfully.");
        return 0;
    }

    public static async Task<int> UninstallServiceAsync()
    {
        Console.WriteLine($"Uninstalling Windows Service '{ServiceName}'...");

        if (!IsServiceInstalled())
        {
            Console.WriteLine($"Service '{ServiceName}' is not installed.");
            return 0;
        }

        // Stop if running
        try
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending)
            {
                Console.WriteLine("Stopping active service...");
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Notice while stopping service: {ex.Message}");
        }

        int exitCode = RunScCommand($"delete \"{ServiceName}\"");
        if (exitCode != 0)
        {
            Console.Error.WriteLine($"Failed to delete service '{ServiceName}'. Error code: {exitCode}. Ensure running as Administrator.");
            return exitCode;
        }

        Console.WriteLine($"Windows Service '{ServiceName}' deleted successfully.");
        return 0;
    }

    public static async Task<int> StartServiceAsync()
    {
        if (!IsServiceInstalled())
        {
            Console.Error.WriteLine($"Service '{ServiceName}' is not installed. Run with --install first.");
            return 1;
        }

        try
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status == ServiceControllerStatus.Running)
            {
                Console.WriteLine($"Service '{ServiceName}' is already running.");
                return 0;
            }

            Console.WriteLine($"Starting service '{ServiceName}'...");
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            Console.WriteLine($"Service '{ServiceName}' started successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to start service: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> StopServiceAsync()
    {
        if (!IsServiceInstalled())
        {
            Console.WriteLine($"Service '{ServiceName}' is not installed.");
            return 0;
        }

        try
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status == ServiceControllerStatus.Stopped)
            {
                Console.WriteLine($"Service '{ServiceName}' is already stopped.");
                return 0;
            }

            Console.WriteLine($"Stopping service '{ServiceName}'...");
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            Console.WriteLine($"Service '{ServiceName}' stopped successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to stop service: {ex.Message}");
            return 1;
        }
    }

    public static void PrintStatus()
    {
        var status = GetServiceStatus();
        if (status.HasValue)
        {
            Console.WriteLine($"Service '{ServiceName}': Installed ({status.Value})");
        }
        else
        {
            Console.WriteLine($"Service '{ServiceName}': Not installed.");
        }
    }

    private static int RunScCommand(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return -1;

        process.WaitForExit(10000);
        return process.ExitCode;
    }
}

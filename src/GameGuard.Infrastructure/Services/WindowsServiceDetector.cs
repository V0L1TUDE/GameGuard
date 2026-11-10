using System.Diagnostics;
using System.Runtime.Versioning;

namespace GameGuard.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public static class WindowsServiceDetector
{
    private const string ServiceName = "GameGuard";

    public static bool IsServiceRunning()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query \"{ServiceName}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            return process.ExitCode == 0 && output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

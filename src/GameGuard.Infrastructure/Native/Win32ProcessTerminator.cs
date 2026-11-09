using System.Runtime.InteropServices;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Extensions.Logging;

namespace GameGuard.Infrastructure.Native;

public sealed class Win32ProcessTerminator : IProcessTerminator
{
    private const uint WM_CLOSE = 0x0010;
    private const uint SYNCHRONIZE = 0x00100000;
    private const uint PROCESS_TERMINATE = 0x0001;
    private const uint WAIT_OBJECT_0 = 0x00000000;
    private const uint WAIT_TIMEOUT = 0x00000102;

    private readonly ILogger<Win32ProcessTerminator> _logger;

    public Win32ProcessTerminator(ILogger<Win32ProcessTerminator> logger)
    {
        _logger = logger;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessTerminateHandle OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessTerminateHandle hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessTerminateHandle hHandle, uint dwMilliseconds);

    public async Task<(bool Success, EnforcementMethod Method)> TerminateProcessAsync(
        int processId,
        string expectedProcessName,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken = default)
    {
        if (processId <= 4)
        {
            _logger.LogWarning("Refusing to terminate system PID {Pid}.", processId);
            return (false, EnforcementMethod.Failed);
        }

        uint pid = unchecked((uint)processId);

        // Phase 1: Graceful Close via WM_CLOSE
        _logger.LogInformation("Attempting graceful termination for PID {Pid} ({ProcessName})...", processId, expectedProcessName);
        PostCloseMessageToWindows(pid);

        // Wait up to gracefulTimeout for process exit
        using (var syncHandle = OpenProcess(SYNCHRONIZE, false, pid))
        {
            if (syncHandle.IsInvalid)
            {
                // Process may have already exited
                _logger.LogInformation("Process PID {Pid} exited or handle could not be opened for sync.", processId);
                return (true, EnforcementMethod.GracefulClose);
            }

            var startWait = DateTime.UtcNow;
            while ((DateTime.UtcNow - startWait) < gracefulTimeout)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return (false, EnforcementMethod.Failed);
                }

                uint waitResult = WaitForSingleObject(syncHandle, 250);
                if (waitResult == WAIT_OBJECT_0)
                {
                    _logger.LogInformation("PID {Pid} terminated gracefully.", processId);
                    return (true, EnforcementMethod.GracefulClose);
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }

        // Phase 2: Controlled Forced Termination
        _logger.LogWarning("Graceful timeout expired for PID {Pid}. Applying forced termination.", processId);
        using (var termHandle = OpenProcess(PROCESS_TERMINATE, false, pid))
        {
            if (termHandle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                // If error is ERROR_INVALID_PARAMETER (87), process is already dead
                if (err == 87)
                {
                    _logger.LogInformation("PID {Pid} has already exited.", processId);
                    return (true, EnforcementMethod.GracefulClose);
                }

                _logger.LogError("Failed to open PID {Pid} with PROCESS_TERMINATE. Win32Error: {ErrorCode}", processId, err);
                return (false, EnforcementMethod.Failed);
            }

            if (TerminateProcess(termHandle, 0))
            {
                _logger.LogInformation("Successfully force-terminated PID {Pid}.", processId);
                return (true, EnforcementMethod.ForcedTermination);
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                _logger.LogError("TerminateProcess failed for PID {Pid}. Win32Error: {ErrorCode}", processId, err);
                return (false, EnforcementMethod.Failed);
            }
        }
    }

    private static void PostCloseMessageToWindows(uint targetPid)
    {
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint windowPid);
            if (windowPid == targetPid)
            {
                PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);
    }
}

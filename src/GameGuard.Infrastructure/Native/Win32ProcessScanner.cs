using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;
using Microsoft.Extensions.Logging;

namespace GameGuard.Infrastructure.Native;

public sealed class Win32ProcessScanner : IProcessScanner
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int MAX_PATH = 1024;

    private readonly ILogger<Win32ProcessScanner> _logger;

    public Win32ProcessScanner(ILogger<Win32ProcessScanner> logger)
    {
        _logger = logger;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeSnapshotHandle CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(SafeSnapshotHandle hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(SafeSnapshotHandle hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessQueryHandle OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(
        SafeProcessQueryHandle hProcess,
        uint dwFlags,
        StringBuilder lpExeName,
        ref uint lpdwSize);

    public IReadOnlyList<DetectedProcessInfo> ScanProcesses()
    {
        var processes = new List<DetectedProcessInfo>(128);

        using var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogWarning("Failed to create Toolhelp snapshot. Win32Error: {ErrorCode}", error);
            return processes;
        }

        var entry = new PROCESSENTRY32W();
        entry.dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>();

        if (!Process32FirstW(snapshot, ref entry))
        {
            return processes;
        }

        do
        {
            int pid = unchecked((int)entry.th32ProcessID);
            int parentPid = unchecked((int)entry.th32ParentProcessID);
            string exeName = entry.szExeFile;

            // System Idle Process (PID 0)
            if (pid <= 0 || string.IsNullOrWhiteSpace(exeName))
            {
                continue;
            }

            string? fullPath = TryGetProcessPath(entry.th32ProcessID);

            processes.Add(new DetectedProcessInfo(
                ProcessId: pid,
                ProcessName: exeName,
                ExecutablePath: fullPath,
                ParentProcessId: parentPid));

        } while (Process32NextW(snapshot, ref entry));

        return processes;
    }

    private string? TryGetProcessPath(uint processId)
    {
        // Safe, read-only limited query. Never touches process memory, never interferes with anti-cheat.
        using var processHandle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (processHandle.IsInvalid)
        {
            return null;
        }

        var sb = new StringBuilder(MAX_PATH);
        uint capacity = (uint)sb.Capacity;

        if (QueryFullProcessImageNameW(processHandle, 0, sb, ref capacity))
        {
            return sb.ToString();
        }

        return null;
    }
}

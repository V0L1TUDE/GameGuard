using System.Diagnostics;
using GameGuard.Infrastructure.Native;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class Win32ProcessScannerTests
{
    private readonly Win32ProcessScanner _scanner = new(NullLogger<Win32ProcessScanner>.Instance);

    [Fact]
    public void ScanProcesses_OnWindows_ReturnsRunningProcesses()
    {
        var processes = _scanner.ScanProcesses();

        Assert.NotEmpty(processes);
        Assert.All(processes, p =>
        {
            Assert.True(p.ProcessId > 0);
            Assert.False(string.IsNullOrWhiteSpace(p.ProcessName));
        });
    }

    [Fact]
    public void ScanProcesses_LocatesCurrentTestRunnerProcess()
    {
        int currentPid = Process.GetCurrentProcess().Id;
        var processes = _scanner.ScanProcesses();

        var currentProcess = processes.FirstOrDefault(p => p.ProcessId == currentPid);

        Assert.NotNull(currentProcess);
        Assert.Equal(currentPid, currentProcess.ProcessId);
        Assert.False(string.IsNullOrWhiteSpace(currentProcess.ProcessName));
    }

    [Fact]
    public void ScanProcesses_CanBeCalledRepeatedlyWithoutErrors()
    {
        for (int i = 0; i < 5; i++)
        {
            var processes = _scanner.ScanProcesses();
            Assert.NotEmpty(processes);
        }
    }
}

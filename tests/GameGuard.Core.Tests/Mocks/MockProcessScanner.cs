using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockProcessScanner : IProcessScanner
{
    private readonly object _lock = new();
    private readonly List<DetectedProcessInfo> _processes = new();

    public void SetProcesses(IEnumerable<DetectedProcessInfo> processes)
    {
        lock (_lock)
        {
            _processes.Clear();
            _processes.AddRange(processes);
        }
    }

    public void AddProcess(int pid, string name, string? path = null, int parentPid = 0)
    {
        lock (_lock)
        {
            _processes.Add(new DetectedProcessInfo(pid, name, path, parentPid));
        }
    }

    public void RemoveProcess(int pid)
    {
        lock (_lock)
        {
            _processes.RemoveAll(p => p.ProcessId == pid);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _processes.Clear();
        }
    }

    public IReadOnlyList<DetectedProcessInfo> ScanProcesses()
    {
        lock (_lock)
        {
            return _processes.ToList();
        }
    }
}

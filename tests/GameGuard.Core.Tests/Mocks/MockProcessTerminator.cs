using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockProcessTerminator : IProcessTerminator
{
    private readonly object _lock = new();
    private readonly List<TerminationCall> _calls = new();

    public EnforcementMethod MethodToReturn { get; set; } = EnforcementMethod.GracefulClose;
    public bool SuccessToReturn { get; set; } = true;
    public Func<int, string, (bool Success, EnforcementMethod Method)>? CustomHandler { get; set; }

    public IReadOnlyList<TerminationCall> Calls
    {
        get
        {
            lock (_lock)
            {
                return _calls.ToList();
            }
        }
    }

    public Task<(bool Success, EnforcementMethod Method)> TerminateProcessAsync(
        int processId,
        string expectedProcessName,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _calls.Add(new TerminationCall(processId, expectedProcessName, gracefulTimeout));
        }

        if (CustomHandler != null)
        {
            return Task.FromResult(CustomHandler(processId, expectedProcessName));
        }

        return Task.FromResult((SuccessToReturn, MethodToReturn));
    }

    public void Reset()
    {
        lock (_lock)
        {
            _calls.Clear();
        }
    }

    public record TerminationCall(int ProcessId, string ExpectedProcessName, TimeSpan GracefulTimeout);
}

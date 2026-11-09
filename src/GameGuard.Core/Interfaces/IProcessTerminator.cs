using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface IProcessTerminator
{
    Task<(bool Success, EnforcementMethod Method)> TerminateProcessAsync(
        int processId,
        string expectedProcessName,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken = default);
}

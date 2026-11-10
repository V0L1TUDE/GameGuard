using GameGuard.Core.Interfaces;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockDatabaseInitializer : IDatabaseInitializer
{
    public bool Initialized { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Initialized = true;
        return Task.CompletedTask;
    }
}

using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockGameProfileRepository : IGameProfileRepository
{
    private readonly List<GameProfile> _profiles = new();

    public void AddRange(IEnumerable<GameProfile> profiles) => _profiles.AddRange(profiles);

    public Task<GameProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_profiles.FirstOrDefault(p => p.Id == id));
    }

    public Task<IReadOnlyList<GameProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<GameProfile>>(_profiles.ToList());
    }

    public Task<IReadOnlyList<GameProfile>> GetMonitoredAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<GameProfile>>(_profiles.Where(p => p.IsMonitored).ToList());
    }

    public Task<GameProfile?> FindByProcessNameAsync(string processName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_profiles.FirstOrDefault(p => p.IsMonitored && p.MatchesProcess(processName)));
    }

    public Task AddAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        _profiles.Add(profile);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        var existing = _profiles.FindIndex(p => p.Id == profile.Id);
        if (existing >= 0)
        {
            _profiles[existing] = profile;
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _profiles.RemoveAll(p => p.Id == id);
        return Task.CompletedTask;
    }
}

using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface IGameProfileRepository
{
    Task<GameProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GameProfile>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GameProfile>> GetMonitoredAsync(CancellationToken cancellationToken = default);
    Task<GameProfile?> FindByProcessNameAsync(string processName, CancellationToken cancellationToken = default);
    Task AddAsync(GameProfile profile, CancellationToken cancellationToken = default);
    Task UpdateAsync(GameProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

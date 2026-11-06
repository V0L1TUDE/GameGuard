using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface ILimitRepository
{
    Task<DailyLimit?> GetIndividualLimitAsync(Guid gameProfileId, CancellationToken cancellationToken = default);
    Task<DailyLimit?> GetSharedLimitAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailyLimit>> GetAllLimitsAsync(CancellationToken cancellationToken = default);
    Task UpsertLimitAsync(DailyLimit limit, CancellationToken cancellationToken = default);
    Task DeleteLimitAsync(Guid limitId, CancellationToken cancellationToken = default);
}

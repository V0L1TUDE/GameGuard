using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface IAdminOverrideRepository
{
    Task<IReadOnlyList<AdminOverride>> GetActiveOverridesAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<AdminOverride?> GetActiveOverrideForGameAsync(Guid gameProfileId, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task AddOverrideAsync(AdminOverride adminOverride, CancellationToken cancellationToken = default);
    Task RevokeOverrideAsync(Guid overrideId, DateTime revokedAtUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminOverride>> GetOverrideHistoryAsync(int limit = 50, CancellationToken cancellationToken = default);
}

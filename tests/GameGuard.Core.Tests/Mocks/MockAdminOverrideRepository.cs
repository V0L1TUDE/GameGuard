using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockAdminOverrideRepository : IAdminOverrideRepository
{
    public List<AdminOverride> Overrides { get; } = new();

    public Task<IReadOnlyList<AdminOverride>> GetActiveOverridesAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<AdminOverride>>(Overrides.Where(o => o.IsActive(nowUtc)).ToList());
    }

    public Task<AdminOverride?> GetActiveOverrideForGameAsync(Guid gameProfileId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Overrides.FirstOrDefault(o => o.GameProfileId == gameProfileId && o.IsActive(nowUtc)));
    }

    public Task AddOverrideAsync(AdminOverride adminOverride, CancellationToken cancellationToken = default)
    {
        Overrides.Add(adminOverride);
        return Task.CompletedTask;
    }

    public Task RevokeOverrideAsync(Guid overrideId, DateTime revokedAtUtc, CancellationToken cancellationToken = default)
    {
        var match = Overrides.FirstOrDefault(o => o.Id == overrideId);
        if (match != null)
        {
            match.IsRevoked = true;
            match.RevokedAtUtc = revokedAtUtc;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AdminOverride>> GetOverrideHistoryAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<AdminOverride>>(Overrides.Take(limit).ToList());
    }
}

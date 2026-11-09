using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface IRelaunchBlockRepository
{
    Task RecordBlockAsync(RelaunchBlockRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RelaunchBlockRecord>> GetRecentBlocksAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<int> GetBlockCountForGameTodayAsync(Guid gameProfileId, DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken cancellationToken = default);
}

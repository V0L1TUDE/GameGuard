using GameGuard.Core.Models;

namespace GameGuard.Core.Interfaces;

public interface ILimitCalculator
{
    LimitCalculationResult Calculate(
        Guid gameProfileId,
        string gameName,
        DailyLimit? individualLimit,
        DailyLimit? sharedLimit,
        int individualSecondsUsedToday,
        int totalSharedSecondsUsedToday,
        int activeSessionDurationSeconds = 0);
}

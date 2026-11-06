using GameGuard.Core.Interfaces;
using GameGuard.Core.Models;

namespace GameGuard.Core.Services;

public sealed class LimitCalculator : ILimitCalculator
{
    public LimitCalculationResult Calculate(
        Guid gameProfileId,
        string gameName,
        DailyLimit? individualLimit,
        DailyLimit? sharedLimit,
        int individualSecondsUsedToday,
        int totalSharedSecondsUsedToday,
        int activeSessionDurationSeconds = 0)
    {
        bool hasIndividual = individualLimit is { IsActive: true, DailyLimitMinutes: > 0 };
        bool hasShared = sharedLimit is { IsActive: true, DailyLimitMinutes: > 0 };

        int totalIndividualUsageSeconds = Math.Max(0, individualSecondsUsedToday + activeSessionDurationSeconds);
        int totalSharedUsageSeconds = Math.Max(0, totalSharedSecondsUsedToday + activeSessionDurationSeconds);
        int usageMinutesToday = (int)Math.Floor(totalIndividualUsageSeconds / 60.0);

        if (!hasIndividual && !hasShared)
        {
            return new LimitCalculationResult
            {
                GameProfileId = gameProfileId,
                GameName = gameName,
                IndividualLimitMinutes = null,
                SharedLimitMinutes = null,
                EffectiveLimitMinutes = null,
                MinutesUsedToday = usageMinutesToday,
                SecondsUsedToday = totalIndividualUsageSeconds,
                SecondsRemaining = null,
                IsInGracePeriod = false,
                GracePeriodSecondsRemaining = 0,
                IsSharedLimitBinding = false
            };
        }

        int? individualRemaining = null;
        if (hasIndividual)
        {
            int allowed = individualLimit!.DailyLimitMinutes * 60;
            individualRemaining = allowed - totalIndividualUsageSeconds;
        }

        int? sharedRemaining = null;
        if (hasShared)
        {
            int allowed = sharedLimit!.DailyLimitMinutes * 60;
            sharedRemaining = allowed - totalSharedUsageSeconds;
        }

        int effectiveRemaining;
        bool isSharedBinding;
        int applicableGracePeriod;
        int effectiveLimitMinutes;

        if (hasIndividual && hasShared)
        {
            if (sharedRemaining!.Value <= individualRemaining!.Value)
            {
                effectiveRemaining = sharedRemaining.Value;
                isSharedBinding = true;
                applicableGracePeriod = sharedLimit!.GracePeriodSeconds;
            }
            else
            {
                effectiveRemaining = individualRemaining.Value;
                isSharedBinding = false;
                applicableGracePeriod = individualLimit!.GracePeriodSeconds;
            }

            effectiveLimitMinutes = Math.Min(individualLimit!.DailyLimitMinutes, sharedLimit!.DailyLimitMinutes);
        }
        else if (hasIndividual)
        {
            effectiveRemaining = individualRemaining!.Value;
            isSharedBinding = false;
            applicableGracePeriod = individualLimit!.GracePeriodSeconds;
            effectiveLimitMinutes = individualLimit.DailyLimitMinutes;
        }
        else
        {
            effectiveRemaining = sharedRemaining!.Value;
            isSharedBinding = true;
            applicableGracePeriod = sharedLimit!.GracePeriodSeconds;
            effectiveLimitMinutes = sharedLimit.DailyLimitMinutes;
        }

        bool isInGracePeriod = false;
        int graceRemaining = 0;

        if (effectiveRemaining <= 0)
        {
            int overdueSeconds = Math.Abs(effectiveRemaining);
            if (overdueSeconds < applicableGracePeriod)
            {
                isInGracePeriod = true;
                graceRemaining = applicableGracePeriod - overdueSeconds;
            }
            else
            {
                isInGracePeriod = false;
                graceRemaining = 0;
            }
        }

        return new LimitCalculationResult
        {
            GameProfileId = gameProfileId,
            GameName = gameName,
            IndividualLimitMinutes = hasIndividual ? individualLimit!.DailyLimitMinutes : null,
            SharedLimitMinutes = hasShared ? sharedLimit!.DailyLimitMinutes : null,
            EffectiveLimitMinutes = effectiveLimitMinutes,
            MinutesUsedToday = usageMinutesToday,
            SecondsUsedToday = totalIndividualUsageSeconds,
            SecondsRemaining = effectiveRemaining,
            IsInGracePeriod = isInGracePeriod,
            GracePeriodSecondsRemaining = graceRemaining,
            IsSharedLimitBinding = isSharedBinding
        };
    }
}

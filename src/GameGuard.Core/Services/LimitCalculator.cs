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
        int activeSessionDurationSeconds = 0,
        AdminOverride? activeOverride = null)
    {
        int totalIndividualUsageSeconds = Math.Max(0, individualSecondsUsedToday + activeSessionDurationSeconds);
        int totalSharedUsageSeconds = Math.Max(0, totalSharedSecondsUsedToday + activeSessionDurationSeconds);
        int usageMinutesToday = (int)Math.Floor(totalIndividualUsageSeconds / 60.0);

        // Check for Emergency Lock override (immediate enforcement)
        if (activeOverride is { Type: OverrideType.EmergencyLock })
        {
            return new LimitCalculationResult
            {
                GameProfileId = gameProfileId,
                GameName = gameName,
                IndividualLimitMinutes = individualLimit?.DailyLimitMinutes,
                SharedLimitMinutes = sharedLimit?.DailyLimitMinutes,
                EffectiveLimitMinutes = 0,
                MinutesUsedToday = usageMinutesToday,
                SecondsUsedToday = totalIndividualUsageSeconds,
                SecondsRemaining = 0,
                IsInGracePeriod = false,
                GracePeriodSecondsRemaining = 0,
                IsSharedLimitBinding = false,
                HasActiveOverride = true,
                ActiveOverrideType = OverrideType.EmergencyLock,
                OverrideExpiresAtUtc = activeOverride.ExpiresAtUtc,
                BonusMinutesApplied = 0
            };
        }

        // Check for Temporary Unlock override (unrestricted access until expiration)
        if (activeOverride is { Type: OverrideType.TemporaryUnlock })
        {
            return new LimitCalculationResult
            {
                GameProfileId = gameProfileId,
                GameName = gameName,
                IndividualLimitMinutes = individualLimit?.DailyLimitMinutes,
                SharedLimitMinutes = sharedLimit?.DailyLimitMinutes,
                EffectiveLimitMinutes = null,
                MinutesUsedToday = usageMinutesToday,
                SecondsUsedToday = totalIndividualUsageSeconds,
                SecondsRemaining = int.MaxValue,
                IsInGracePeriod = false,
                GracePeriodSecondsRemaining = 0,
                IsSharedLimitBinding = false,
                HasActiveOverride = true,
                ActiveOverrideType = OverrideType.TemporaryUnlock,
                OverrideExpiresAtUtc = activeOverride.ExpiresAtUtc,
                BonusMinutesApplied = 0
            };
        }

        bool hasIndividual = individualLimit is { IsActive: true, DailyLimitMinutes: > 0 };
        bool hasShared = sharedLimit is { IsActive: true, DailyLimitMinutes: > 0 };
        int bonusMinutes = activeOverride is { Type: OverrideType.BonusTime } ? activeOverride.BonusMinutes : 0;
        int bonusSeconds = bonusMinutes * 60;

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
                IsSharedLimitBinding = false,
                HasActiveOverride = bonusMinutes > 0,
                ActiveOverrideType = bonusMinutes > 0 ? OverrideType.BonusTime : null,
                OverrideExpiresAtUtc = activeOverride?.ExpiresAtUtc,
                BonusMinutesApplied = bonusMinutes
            };
        }

        int? individualRemaining = null;
        if (hasIndividual)
        {
            int allowed = (individualLimit!.DailyLimitMinutes * 60) + bonusSeconds;
            individualRemaining = allowed - totalIndividualUsageSeconds;
        }

        int? sharedRemaining = null;
        if (hasShared)
        {
            int allowed = (sharedLimit!.DailyLimitMinutes * 60) + bonusSeconds;
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

            effectiveLimitMinutes = Math.Min(individualLimit!.DailyLimitMinutes, sharedLimit!.DailyLimitMinutes) + bonusMinutes;
        }
        else if (hasIndividual)
        {
            effectiveRemaining = individualRemaining!.Value;
            isSharedBinding = false;
            applicableGracePeriod = individualLimit!.GracePeriodSeconds;
            effectiveLimitMinutes = individualLimit.DailyLimitMinutes + bonusMinutes;
        }
        else
        {
            effectiveRemaining = sharedRemaining!.Value;
            isSharedBinding = true;
            applicableGracePeriod = sharedLimit!.GracePeriodSeconds;
            effectiveLimitMinutes = sharedLimit.DailyLimitMinutes + bonusMinutes;
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
            IsSharedLimitBinding = isSharedBinding,
            HasActiveOverride = bonusMinutes > 0,
            ActiveOverrideType = bonusMinutes > 0 ? OverrideType.BonusTime : null,
            OverrideExpiresAtUtc = activeOverride?.ExpiresAtUtc,
            BonusMinutesApplied = bonusMinutes
        };
    }
}

namespace GameGuard.Core.Models;

public sealed record LimitCalculationResult
{
    public required Guid GameProfileId { get; init; }
    public required string GameName { get; init; }
    public int? IndividualLimitMinutes { get; init; }
    public int? SharedLimitMinutes { get; init; }
    public int? EffectiveLimitMinutes { get; init; }
    public int MinutesUsedToday { get; init; }
    public int SecondsUsedToday { get; init; }
    public int? SecondsRemaining { get; init; }
    public int? MinutesRemaining => SecondsRemaining.HasValue ? Math.Max(0, (int)Math.Ceiling(SecondsRemaining.Value / 60.0)) : null;
    public bool HasLimit => EffectiveLimitMinutes.HasValue;

    public bool HasActiveOverride { get; init; }
    public OverrideType? ActiveOverrideType { get; init; }
    public DateTime? OverrideExpiresAtUtc { get; init; }
    public int BonusMinutesApplied { get; init; }

    public bool IsLimitExceeded
    {
        get
        {
            if (ActiveOverrideType == OverrideType.EmergencyLock)
            {
                return true;
            }

            if (ActiveOverrideType == OverrideType.TemporaryUnlock)
            {
                return false;
            }

            return SecondsRemaining.HasValue && SecondsRemaining.Value <= 0;
        }
    }

    public bool IsInGracePeriod { get; init; }
    public int GracePeriodSecondsRemaining { get; init; }
    public bool IsSharedLimitBinding { get; init; }
}

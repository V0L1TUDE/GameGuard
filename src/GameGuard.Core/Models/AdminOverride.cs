namespace GameGuard.Core.Models;

public sealed class AdminOverride
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? GameProfileId { get; set; }
    public OverrideType Type { get; set; }
    public int BonusMinutes { get; set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public string? Reason { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public bool IsActive(DateTime nowUtc) => !IsRevoked && nowUtc < ExpiresAtUtc;

    public static AdminOverride CreateBonusTime(Guid? gameProfileId, int bonusMinutes, DateTime nowUtc, TimeSpan validFor, string? reason = null)
    {
        return new AdminOverride
        {
            Id = Guid.NewGuid(),
            GameProfileId = gameProfileId,
            Type = OverrideType.BonusTime,
            BonusMinutes = bonusMinutes,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(validFor),
            Reason = reason,
            IsRevoked = false
        };
    }

    public static AdminOverride CreateTemporaryUnlock(Guid? gameProfileId, DateTime nowUtc, TimeSpan duration, string? reason = null)
    {
        return new AdminOverride
        {
            Id = Guid.NewGuid(),
            GameProfileId = gameProfileId,
            Type = OverrideType.TemporaryUnlock,
            BonusMinutes = 0,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(duration),
            Reason = reason,
            IsRevoked = false
        };
    }

    public static AdminOverride CreateEmergencyLock(Guid? gameProfileId, DateTime nowUtc, TimeSpan duration, string? reason = null)
    {
        return new AdminOverride
        {
            Id = Guid.NewGuid(),
            GameProfileId = gameProfileId,
            Type = OverrideType.EmergencyLock,
            BonusMinutes = 0,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(duration),
            Reason = reason,
            IsRevoked = false
        };
    }
}

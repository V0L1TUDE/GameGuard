namespace GameGuard.Core.Models;

public sealed class DailyLimit
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? GameProfileId { get; set; }
    public int DailyLimitMinutes { get; set; } = 120;
    public int ResetHourUtc { get; set; } = 0;
    public bool IsShared { get; set; }
    public bool IsActive { get; set; } = true;
    public int GracePeriodSeconds { get; set; } = 60;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    public static DailyLimit CreateIndividual(Guid gameProfileId, int minutes, int gracePeriodSeconds = 60)
    {
        return new DailyLimit
        {
            Id = Guid.NewGuid(),
            GameProfileId = gameProfileId,
            DailyLimitMinutes = minutes,
            IsShared = false,
            IsActive = true,
            GracePeriodSeconds = gracePeriodSeconds,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public static DailyLimit CreateShared(int minutes, int gracePeriodSeconds = 60)
    {
        return new DailyLimit
        {
            Id = Guid.NewGuid(),
            GameProfileId = null,
            DailyLimitMinutes = minutes,
            IsShared = true,
            IsActive = true,
            GracePeriodSeconds = gracePeriodSeconds,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}

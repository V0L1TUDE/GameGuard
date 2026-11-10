using GameGuard.Core.Models;

namespace GameGuard.UI.Services;

public sealed class AdminOverrideCreateModel
{
    public Guid? GameProfileId { get; set; }
    public OverrideType Type { get; set; } = OverrideType.BonusTime;
    public int BonusMinutes { get; set; } = 30;
    public int DurationMinutes { get; set; } = 60;
    public string? Reason { get; set; }
}

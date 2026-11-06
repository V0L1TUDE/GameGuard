namespace GameGuard.Core.Models;

public enum SessionTerminationReason
{
    Active = 0,
    UserClosed = 1,
    LimitEnforced = 2,
    ForcedByAdmin = 3,
    CrashOrExternal = 4
}

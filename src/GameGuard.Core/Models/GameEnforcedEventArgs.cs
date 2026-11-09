namespace GameGuard.Core.Models;

public sealed class GameEnforcedEventArgs : EventArgs
{
    public required EnforcementResult Result { get; init; }
}

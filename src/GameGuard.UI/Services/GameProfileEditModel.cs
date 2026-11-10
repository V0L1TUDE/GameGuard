namespace GameGuard.UI.Services;

public sealed class GameProfileEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string ProcessNames { get; set; } = string.Empty;
    public bool IsMonitored { get; set; } = true;
}

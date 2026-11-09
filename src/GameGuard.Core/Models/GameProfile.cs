namespace GameGuard.Core.Models;

public sealed class GameProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<string> ProcessNames { get; set; } = Array.Empty<string>();
    public string ProcessNamesFormatted => string.Join(", ", ProcessNames);
    public string? ExecutablePath { get; set; }
    public bool IsMonitored { get; set; } = true;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public bool MatchesProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        string cleanTarget = processName.Trim();
        if (cleanTarget.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            cleanTarget = cleanTarget[..^4];
        }

        foreach (var configuredName in ProcessNames)
        {
            string cleanConfig = configuredName.Trim();
            if (cleanConfig.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                cleanConfig = cleanConfig[..^4];
            }

            if (string.Equals(cleanTarget, cleanConfig, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

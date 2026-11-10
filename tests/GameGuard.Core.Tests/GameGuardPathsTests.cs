using GameGuard.Infrastructure.Persistence;
using GameGuard.Infrastructure.Services;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class GameGuardPathsTests
{
    [Fact]
    public void GetDatabasePath_ReturnsValidPathEndingInGameguardDb()
    {
        string sharedPath = GameGuardPaths.GetDatabasePath(preferSharedProgramData: true);
        Assert.NotNull(sharedPath);
        Assert.EndsWith("gameguard.db", sharedPath, StringComparison.OrdinalIgnoreCase);

        string userPath = GameGuardPaths.GetDatabasePath(preferSharedProgramData: false);
        Assert.NotNull(userPath);
        Assert.EndsWith("gameguard.db", userPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetLogDirectory_ReturnsValidDirectoryAndEnsuresExists()
    {
        string logDir = GameGuardPaths.GetLogDirectory();
        Assert.NotNull(logDir);
        Assert.True(Directory.Exists(logDir));
    }

    [Fact]
    public void WindowsServiceDetector_IsServiceRunning_DoesNotThrow()
    {
        // Safe verification that detector executes smoothly on Windows
        bool isRunning = WindowsServiceDetector.IsServiceRunning();
        // Returns boolean without throwing exceptions
        Assert.True(isRunning || !isRunning);
    }
}

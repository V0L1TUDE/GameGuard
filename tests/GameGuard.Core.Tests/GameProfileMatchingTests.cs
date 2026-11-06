using GameGuard.Core.Models;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class GameProfileMatchingTests
{
    [Theory]
    [InlineData("cs2.exe", true)]
    [InlineData("cs2", true)]
    [InlineData("CS2.EXE", true)]
    [InlineData("Cs2", true)]
    [InlineData("csgo.exe", false)]
    [InlineData("valorant.exe", false)]
    [InlineData("", false)]
    public void MatchesProcess_WithCounterStrike2Profile_MatchesCorrectly(string processName, bool expected)
    {
        var profile = new GameProfile
        {
            Name = "Counter-Strike 2",
            ProcessNames = new[] { "cs2.exe" }
        };

        var actual = profile.MatchesProcess(processName);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("VALORANT-Win64-Shipping.exe", true)]
    [InlineData("valorant-win64-shipping", true)]
    [InlineData("VALORANT.exe", true)]
    [InlineData("VALORANT", true)]
    [InlineData("RiotClientServices.exe", false)]
    public void MatchesProcess_WithValorantProfile_MatchesAliases(string processName, bool expected)
    {
        var profile = new GameProfile
        {
            Name = "Valorant",
            ProcessNames = new[] { "VALORANT-Win64-Shipping.exe", "VALORANT.exe" }
        };

        var actual = profile.MatchesProcess(processName);
        Assert.Equal(expected, actual);
    }
}

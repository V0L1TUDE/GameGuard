using GameGuard.Core.Models;
using GameGuard.Core.Services;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class LimitCalculatorTests
{
    private readonly LimitCalculator _calculator = new();
    private readonly Guid _cs2Id = Guid.NewGuid();
    private readonly Guid _valorantId = Guid.NewGuid();

    [Fact]
    public void Calculate_WhenNoLimitsConfigured_ReturnsNoLimits()
    {
        var result = _calculator.Calculate(
            gameProfileId: _cs2Id,
            gameName: "Counter-Strike 2",
            individualLimit: null,
            sharedLimit: null,
            individualSecondsUsedToday: 3600,
            totalSharedSecondsUsedToday: 3600);

        Assert.False(result.HasLimit);
        Assert.Null(result.SecondsRemaining);
        Assert.Null(result.MinutesRemaining);
        Assert.False(result.IsLimitExceeded);
        Assert.False(result.IsInGracePeriod);
        Assert.Equal(60, result.MinutesUsedToday);
    }

    [Fact]
    public void Calculate_WithIndividualLimitOnly_ReturnsAccurateRemainingTime()
    {
        var limit = DailyLimit.CreateIndividual(_cs2Id, minutes: 60, gracePeriodSeconds: 60);

        // 40 minutes used so far (2400 seconds)
        var result = _calculator.Calculate(
            gameProfileId: _cs2Id,
            gameName: "Counter-Strike 2",
            individualLimit: limit,
            sharedLimit: null,
            individualSecondsUsedToday: 2400,
            totalSharedSecondsUsedToday: 2400);

        Assert.True(result.HasLimit);
        Assert.Equal(60, result.EffectiveLimitMinutes);
        Assert.Equal(1200, result.SecondsRemaining); // 20 minutes remaining
        Assert.Equal(20, result.MinutesRemaining);
        Assert.False(result.IsLimitExceeded);
        Assert.False(result.IsInGracePeriod);
        Assert.False(result.IsSharedLimitBinding);
    }

    [Fact]
    public void Calculate_WhenTimeExpires_EntersGracePeriod()
    {
        var limit = DailyLimit.CreateIndividual(_cs2Id, minutes: 60, gracePeriodSeconds: 120);

        // Exactly 60 minutes and 30 seconds used (30 seconds into grace period)
        var result = _calculator.Calculate(
            gameProfileId: _cs2Id,
            gameName: "Counter-Strike 2",
            individualLimit: limit,
            sharedLimit: null,
            individualSecondsUsedToday: 3630,
            totalSharedSecondsUsedToday: 3630);

        Assert.True(result.HasLimit);
        Assert.True(result.IsLimitExceeded);
        Assert.True(result.IsInGracePeriod);
        Assert.Equal(90, result.GracePeriodSecondsRemaining); // 120 - 30 = 90 seconds
    }

    [Fact]
    public void Calculate_WhenGracePeriodExhausted_RequiresImmediateTermination()
    {
        var limit = DailyLimit.CreateIndividual(_cs2Id, minutes: 60, gracePeriodSeconds: 60);

        // 60 minutes + 65 seconds used
        var result = _calculator.Calculate(
            gameProfileId: _cs2Id,
            gameName: "Counter-Strike 2",
            individualLimit: limit,
            sharedLimit: null,
            individualSecondsUsedToday: 3665,
            totalSharedSecondsUsedToday: 3665);

        Assert.True(result.IsLimitExceeded);
        Assert.False(result.IsInGracePeriod);
        Assert.Equal(0, result.GracePeriodSecondsRemaining);
    }

    [Fact]
    public void Calculate_WhenSharedLimitIsMoreRestrictive_BindsToSharedLimit()
    {
        var individualLimit = DailyLimit.CreateIndividual(_cs2Id, minutes: 120); // 2 hours individual
        var sharedLimit = DailyLimit.CreateShared(minutes: 90); // 1.5 hours total for all games

        // CS2 used 30 minutes, but Valorant used 50 minutes earlier today
        // Total shared usage = 80 minutes (4800s). Shared remaining = 10 minutes (600s).
        // Individual remaining for CS2 = 90 minutes.
        // Therefore, Shared limit must be the binding constraint!
        var result = _calculator.Calculate(
            gameProfileId: _cs2Id,
            gameName: "Counter-Strike 2",
            individualLimit: individualLimit,
            sharedLimit: sharedLimit,
            individualSecondsUsedToday: 1800,
            totalSharedSecondsUsedToday: 4800);

        Assert.True(result.HasLimit);
        Assert.True(result.IsSharedLimitBinding);
        Assert.Equal(600, result.SecondsRemaining); // 10 minutes remaining
        Assert.Equal(10, result.MinutesRemaining);
        Assert.False(result.IsLimitExceeded);
    }

    [Fact]
    public void Calculate_WhenIndividualLimitIsMoreRestrictive_BindsToIndividualLimit()
    {
        var individualLimit = DailyLimit.CreateIndividual(_cs2Id, minutes: 30); // 30 mins individual
        var sharedLimit = DailyLimit.CreateShared(minutes: 180); // 3 hours shared

        // CS2 used 25 mins (1500s). Total shared used = 25 mins.
        // Individual remaining = 5 minutes (300s).
        // Shared remaining = 155 minutes.
        // Individual limit must bind.
        var result = _calculator.Calculate(
            gameProfileId: _cs2Id,
            gameName: "Counter-Strike 2",
            individualLimit: individualLimit,
            sharedLimit: sharedLimit,
            individualSecondsUsedToday: 1500,
            totalSharedSecondsUsedToday: 1500);

        Assert.True(result.HasLimit);
        Assert.False(result.IsSharedLimitBinding);
        Assert.Equal(300, result.SecondsRemaining);
        Assert.Equal(5, result.MinutesRemaining);
    }

    [Fact]
    public void Calculate_IncludesActiveSessionDuration()
    {
        var limit = DailyLimit.CreateIndividual(_cs2Id, minutes: 60);

        // 50 minutes completed previously + 5 minutes in current active session = 55 minutes
        var result = _calculator.Calculate(
            gameProfileId: _cs2Id,
            gameName: "Counter-Strike 2",
            individualLimit: limit,
            sharedLimit: null,
            individualSecondsUsedToday: 3000,
            totalSharedSecondsUsedToday: 3000,
            activeSessionDurationSeconds: 300);

        Assert.Equal(3300, result.SecondsUsedToday);
        Assert.Equal(300, result.SecondsRemaining); // 5 minutes remaining
        Assert.Equal(5, result.MinutesRemaining);
        Assert.False(result.IsLimitExceeded);
    }
}

using GameGuard.Core.Models;
using GameGuard.Core.Services;
using GameGuard.Core.Tests.Mocks;
using GameGuard.Infrastructure.Analytics;
using GameGuard.Infrastructure.Limits;
using GameGuard.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class SessionAnalyticsServiceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteDatabaseInitializer _initializer;
    private readonly SqliteGameProfileRepository _gameRepo;
    private readonly SqliteLimitRepository _limitRepo;
    private readonly SqliteSessionRepository _sessionRepo;
    private readonly SqliteRelaunchBlockRepository _blockRepo;
    private readonly DayBoundaryCalculator _boundaryCalc;
    private readonly MockSystemClock _clock;
    private readonly SessionAnalyticsService _analyticsService;

    public SessionAnalyticsServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"gameguard_analytics_{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new SqliteDatabaseInitializer(_connectionFactory, NullLogger<SqliteDatabaseInitializer>.Instance);
        _initializer.InitializeAsync().GetAwaiter().GetResult();

        _gameRepo = new SqliteGameProfileRepository(_connectionFactory);
        _limitRepo = new SqliteLimitRepository(_connectionFactory);
        _sessionRepo = new SqliteSessionRepository(_connectionFactory);
        _blockRepo = new SqliteRelaunchBlockRepository(_connectionFactory);

        _boundaryCalc = new DayBoundaryCalculator();
        _clock = new MockSystemClock { UtcNow = new DateTime(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc) };

        _analyticsService = new SessionAnalyticsService(
            _sessionRepo,
            _blockRepo,
            _gameRepo,
            _limitRepo,
            _boundaryCalc,
            _clock,
            TimeZoneInfo.Utc);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task GetAnalyticsSummaryAsync_CalculatesDailyWeeklyAndMonthlyTotalsCorrectly()
    {
        var games = await _gameRepo.GetAllAsync();
        var cs2 = games.First(g => g.ProcessNames.Contains("cs2.exe"));
        var valorant = games.First(g => g.ProcessNames.Contains("VALORANT-Win64-Shipping.exe"));

        var now = _clock.UtcNow;

        // Session 1: Today, CS2, 1800s (30m)
        await _sessionRepo.StartSessionAsync(new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = cs2.Id,
            ProcessId = 1001,
            ProcessName = "cs2.exe",
            StartTimeUtc = now.AddHours(-2),
            EndTimeUtc = now.AddHours(-1.5),
            DurationSeconds = 1800,
            TerminationReason = SessionTerminationReason.UserClosed
        });

        // Session 2: Today, Valorant, 1200s (20m)
        await _sessionRepo.StartSessionAsync(new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = valorant.Id,
            ProcessId = 1002,
            ProcessName = "VALORANT-Win64-Shipping.exe",
            StartTimeUtc = now.AddMinutes(-40),
            EndTimeUtc = now.AddMinutes(-20),
            DurationSeconds = 1200,
            TerminationReason = SessionTerminationReason.LimitEnforced
        });

        // Session 3: 3 days ago, CS2, 3600s (60m)
        await _sessionRepo.StartSessionAsync(new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = cs2.Id,
            ProcessId = 1003,
            ProcessName = "cs2.exe",
            StartTimeUtc = now.AddDays(-3),
            EndTimeUtc = now.AddDays(-3).AddHours(1),
            DurationSeconds = 3600,
            TerminationReason = SessionTerminationReason.UserClosed
        });

        // Session 4: 15 days ago, Valorant, 7200s (120m)
        await _sessionRepo.StartSessionAsync(new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = valorant.Id,
            ProcessId = 1004,
            ProcessName = "VALORANT-Win64-Shipping.exe",
            StartTimeUtc = now.AddDays(-15),
            EndTimeUtc = now.AddDays(-15).AddHours(2),
            DurationSeconds = 7200,
            TerminationReason = SessionTerminationReason.UserClosed
        });

        // Block record: 2 days ago
        await _blockRepo.RecordBlockAsync(new RelaunchBlockRecord
        {
            Id = Guid.NewGuid(),
            GameProfileId = cs2.Id,
            ProcessId = 2001,
            ProcessName = "cs2.exe",
            BlockedAtUtc = now.AddDays(-2),
            Reason = "Limit exhausted"
        });

        var summary = await _analyticsService.GetAnalyticsSummaryAsync(now);

        // Today: 1800 + 1200 = 3000s = 50m
        Assert.Equal(TimeSpan.FromSeconds(3000), summary.TotalPlaytimeToday);

        // Past 7 Days: 3000 + 3600 = 6600s = 110m
        Assert.Equal(TimeSpan.FromSeconds(6600), summary.TotalPlaytimePast7Days);

        // Past 30 Days: 6600 + 7200 = 13800s
        Assert.Equal(TimeSpan.FromSeconds(13800), summary.TotalPlaytimePast30Days);

        Assert.Equal(4, summary.TotalSessionsCount);
        Assert.Equal(1, summary.TotalBlockedRelaunchesCount);

        // Check breakdowns
        Assert.Equal(2, summary.GameBreakdowns.Count);

        // Valorant has 1200 + 7200 = 8400s (60.9%)
        // CS2 has 1800 + 3600 = 5400s (39.1%)
        var valBreakdown = summary.GameBreakdowns.First(b => b.GameProfileId == valorant.Id);
        Assert.Equal(TimeSpan.FromSeconds(8400), valBreakdown.TotalPlaytime);
        Assert.Equal(2, valBreakdown.SessionCount);
        Assert.True(valBreakdown.PercentageOfTotal > 60.0);

        var csBreakdown = summary.GameBreakdowns.First(b => b.GameProfileId == cs2.Id);
        Assert.Equal(TimeSpan.FromSeconds(5400), csBreakdown.TotalPlaytime);
        Assert.Equal(2, csBreakdown.SessionCount);
        Assert.True(csBreakdown.PercentageOfTotal > 38.0);

        // Termination reasons
        Assert.Equal(3, summary.TerminationBreakdowns[SessionTerminationReason.UserClosed]);
        Assert.Equal(1, summary.TerminationBreakdowns[SessionTerminationReason.LimitEnforced]);
    }

    [Fact]
    public async Task GetFilteredSessionsAsync_WithDateRangeAndGameFilter_ReturnsMatchingSessions()
    {
        var games = await _gameRepo.GetAllAsync();
        var cs2 = games.First(g => g.ProcessNames.Contains("cs2.exe"));
        var valorant = games.First(g => g.ProcessNames.Contains("VALORANT-Win64-Shipping.exe"));
        var now = _clock.UtcNow;

        await _sessionRepo.StartSessionAsync(new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = cs2.Id,
            ProcessId = 3001,
            ProcessName = "cs2.exe",
            StartTimeUtc = now.AddHours(-1),
            EndTimeUtc = now,
            DurationSeconds = 3600,
            TerminationReason = SessionTerminationReason.UserClosed
        });

        await _sessionRepo.StartSessionAsync(new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = valorant.Id,
            ProcessId = 3002,
            ProcessName = "VALORANT-Win64-Shipping.exe",
            StartTimeUtc = now.AddHours(-1),
            EndTimeUtc = now,
            DurationSeconds = 3600,
            TerminationReason = SessionTerminationReason.UserClosed
        });

        // Filter by CS2 only
        var cs2Filtered = await _analyticsService.GetFilteredSessionsAsync(
            gameProfileId: cs2.Id,
            fromUtc: now.AddHours(-2),
            toUtc: now.AddHours(1));

        Assert.Single(cs2Filtered);
        Assert.Equal("cs2.exe", cs2Filtered[0].ProcessName);

        // Filter by date range excluding past hour
        var oldFiltered = await _analyticsService.GetFilteredSessionsAsync(
            fromUtc: now.AddDays(-10),
            toUtc: now.AddDays(-5));

        Assert.Empty(oldFiltered);
    }
}

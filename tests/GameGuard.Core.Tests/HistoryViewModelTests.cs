using GameGuard.Core.Models;
using GameGuard.Core.Services;
using GameGuard.Core.Tests.Mocks;
using GameGuard.Infrastructure.Analytics;
using GameGuard.Infrastructure.Limits;
using GameGuard.Infrastructure.Persistence;
using GameGuard.UI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class HistoryViewModelTests : IDisposable
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
    private readonly HistoryViewModel _viewModel;

    public HistoryViewModelTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"gameguard_vm_history_{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new SqliteDatabaseInitializer(_connectionFactory, NullLogger<SqliteDatabaseInitializer>.Instance);
        _initializer.InitializeAsync().GetAwaiter().GetResult();

        _gameRepo = new SqliteGameProfileRepository(_connectionFactory);
        _limitRepo = new SqliteLimitRepository(_connectionFactory);
        _sessionRepo = new SqliteSessionRepository(_connectionFactory);
        _blockRepo = new SqliteRelaunchBlockRepository(_connectionFactory);

        _boundaryCalc = new DayBoundaryCalculator();
        _clock = new MockSystemClock { UtcNow = new DateTime(2026, 11, 10, 16, 0, 0, DateTimeKind.Utc) };

        _analyticsService = new SessionAnalyticsService(
            _sessionRepo,
            _blockRepo,
            _gameRepo,
            _limitRepo,
            _boundaryCalc,
            _clock,
            TimeZoneInfo.Utc);

        _viewModel = new HistoryViewModel(
            _analyticsService,
            _gameRepo,
            _sessionRepo,
            _blockRepo,
            _boundaryCalc,
            _clock);
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
    public async Task InitializeAsync_LoadsAnalyticsAndBreakdowns()
    {
        var games = await _gameRepo.GetAllAsync();
        var cs2 = games.First(g => g.ProcessNames.Contains("cs2.exe"));
        var now = _clock.UtcNow;

        await _sessionRepo.StartSessionAsync(new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = cs2.Id,
            ProcessId = 5001,
            ProcessName = "cs2.exe",
            StartTimeUtc = now.AddHours(-1),
            EndTimeUtc = now,
            DurationSeconds = 3600,
            TerminationReason = SessionTerminationReason.UserClosed
        });

        await _blockRepo.RecordBlockAsync(new RelaunchBlockRecord
        {
            Id = Guid.NewGuid(),
            GameProfileId = cs2.Id,
            ProcessId = 5002,
            ProcessName = "cs2.exe",
            BlockedAtUtc = now.AddMinutes(-10),
            Reason = "Blocked cooldown"
        });

        await _viewModel.InitializeAsync();

        Assert.Equal("1h 0m", _viewModel.TotalTodayFormatted);
        Assert.Equal("1h 0m", _viewModel.Total7DaysFormatted);
        Assert.Equal(1, _viewModel.TotalSessionsCount);
        Assert.Equal(1, _viewModel.TotalBlockedCount);

        Assert.Single(_viewModel.GameBreakdowns);
        Assert.Equal("Counter-Strike 2", _viewModel.GameBreakdowns[0].GameName);
        Assert.Equal(100.0, _viewModel.GameBreakdowns[0].Percentage);

        Assert.Single(_viewModel.Sessions);
        Assert.Equal("Counter-Strike 2", _viewModel.Sessions[0].GameName);
        Assert.Equal("Normal Exit", _viewModel.Sessions[0].TerminationBadgeText);

        Assert.Single(_viewModel.BlockedRelaunches);
    }

    [Fact]
    public async Task ApplyFiltersAsync_FiltersByGameName()
    {
        var games = await _gameRepo.GetAllAsync();
        var cs2 = games.First(g => g.ProcessNames.Contains("cs2.exe"));
        var valorant = games.First(g => g.ProcessNames.Contains("VALORANT-Win64-Shipping.exe"));
        var now = _clock.UtcNow;

        await _sessionRepo.StartSessionAsync(new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = cs2.Id,
            ProcessId = 6001,
            ProcessName = "cs2.exe",
            StartTimeUtc = now.AddHours(-2),
            EndTimeUtc = now.AddHours(-1),
            DurationSeconds = 3600,
            TerminationReason = SessionTerminationReason.UserClosed
        });

        await _sessionRepo.StartSessionAsync(new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = valorant.Id,
            ProcessId = 6002,
            ProcessName = "VALORANT-Win64-Shipping.exe",
            StartTimeUtc = now.AddMinutes(-30),
            EndTimeUtc = now,
            DurationSeconds = 1800,
            TerminationReason = SessionTerminationReason.LimitEnforced
        });

        await _viewModel.InitializeAsync();
        Assert.Equal(2, _viewModel.Sessions.Count);

        // Filter to Valorant only
        _viewModel.SelectedGameFilter = "Valorant";
        // Give background filter task brief time to complete
        await _viewModel.ApplyFiltersAsync();

        Assert.Single(_viewModel.Sessions);
        Assert.Equal("Valorant", _viewModel.Sessions[0].GameName);
        Assert.Equal("Limit Enforced", _viewModel.Sessions[0].TerminationBadgeText);
    }

    [Fact]
    public async Task ApplyFiltersAsync_FiltersByDateRange()
    {
        var games = await _gameRepo.GetAllAsync();
        var cs2 = games.First(g => g.ProcessNames.Contains("cs2.exe"));
        var now = _clock.UtcNow;

        // Session 1: Today
        await _sessionRepo.StartSessionAsync(new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = cs2.Id,
            ProcessId = 7001,
            ProcessName = "cs2.exe",
            StartTimeUtc = now.AddHours(-1),
            EndTimeUtc = now,
            DurationSeconds = 3600,
            TerminationReason = SessionTerminationReason.UserClosed
        });

        // Session 2: 12 days ago
        await _sessionRepo.StartSessionAsync(new GamingSession
        {
            Id = Guid.NewGuid(),
            GameProfileId = cs2.Id,
            ProcessId = 7002,
            ProcessName = "cs2.exe",
            StartTimeUtc = now.AddDays(-12),
            EndTimeUtc = now.AddDays(-12).AddHours(1),
            DurationSeconds = 3600,
            TerminationReason = SessionTerminationReason.UserClosed
        });

        await _viewModel.InitializeAsync();
        Assert.Equal(2, _viewModel.Sessions.Count);

        // Select Past 7 Days
        _viewModel.SelectedDateRange = "Past 7 Days";
        await _viewModel.ApplyFiltersAsync();

        Assert.Single(_viewModel.Sessions);
        Assert.Equal(7001, _viewModel.Sessions[0].ProcessId);
    }
}

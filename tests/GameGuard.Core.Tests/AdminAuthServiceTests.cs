using GameGuard.Core.Tests.Mocks;
using GameGuard.Infrastructure.Persistence;
using GameGuard.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameGuard.Core.Tests;

public sealed class AdminAuthServiceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteDatabaseInitializer _initializer;
    private readonly MockSystemClock _systemClock;
    private readonly SqliteAdminAuthService _authService;

    public AdminAuthServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"gameguard_auth_{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new SqliteDatabaseInitializer(_connectionFactory, NullLogger<SqliteDatabaseInitializer>.Instance);
        _systemClock = new MockSystemClock();

        _authService = new SqliteAdminAuthService(
            _connectionFactory,
            _systemClock,
            NullLogger<SqliteAdminAuthService>.Instance);
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
    public async Task IsPasswordConfiguredAsync_WhenEmpty_ReturnsFalse()
    {
        await _initializer.InitializeAsync();

        bool configured = await _authService.IsPasswordConfiguredAsync();

        Assert.False(configured);
    }

    [Fact]
    public async Task SetPasswordAsync_AndValidatePasswordAsync_ValidatesCorrectPassword()
    {
        await _initializer.InitializeAsync();

        await _authService.SetPasswordAsync("AdminSecret123!");

        bool isConfigured = await _authService.IsPasswordConfiguredAsync();
        Assert.True(isConfigured);

        bool isValid = await _authService.ValidatePasswordAsync("AdminSecret123!");
        Assert.True(isValid);

        bool isInvalid = await _authService.ValidatePasswordAsync("WrongPassword");
        Assert.False(isInvalid);
    }

    [Fact]
    public async Task ValidatePasswordAsync_AfterFiveFailedAttempts_EnforcesLockout()
    {
        await _initializer.InitializeAsync();
        await _authService.SetPasswordAsync("SuperSecretPIN42");

        for (int i = 0; i < 4; i++)
        {
            bool result = await _authService.ValidatePasswordAsync("IncorrectPIN");
            Assert.False(result);
            Assert.False(_authService.IsLockedOut(out _));
        }

        // 5th failed attempt should trigger lockout
        bool fifthResult = await _authService.ValidatePasswordAsync("IncorrectPIN");
        Assert.False(fifthResult);
        Assert.True(_authService.IsLockedOut(out var remaining));
        Assert.True(remaining.TotalMinutes > 4);

        // Even with the correct password, validation fails during lockout
        bool attemptDuringLockout = await _authService.ValidatePasswordAsync("SuperSecretPIN42");
        Assert.False(attemptDuringLockout);

        // Advance system clock past the 5-minute lockout
        _systemClock.Advance(TimeSpan.FromMinutes(6));

        Assert.False(_authService.IsLockedOut(out _));
        bool attemptAfterLockout = await _authService.ValidatePasswordAsync("SuperSecretPIN42");
        Assert.True(attemptAfterLockout);
    }

    [Fact]
    public async Task ChangePasswordAsync_WithValidCurrentPassword_UpdatesPassword()
    {
        await _initializer.InitializeAsync();
        await _authService.SetPasswordAsync("OriginalPassword1");

        bool changed = await _authService.ChangePasswordAsync("OriginalPassword1", "NewPassword999!");
        Assert.True(changed);

        Assert.False(await _authService.ValidatePasswordAsync("OriginalPassword1"));
        Assert.True(await _authService.ValidatePasswordAsync("NewPassword999!"));
    }
}

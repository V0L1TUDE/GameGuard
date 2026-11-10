using GameGuard.Core.Interfaces;

namespace GameGuard.Core.Tests.Mocks;

public sealed class MockAdminAuthService : IAdminAuthService
{
    private string? _configuredPassword;

    public bool IsPasswordConfigured { get; set; } = true;
    public bool IsLockedOutResult { get; set; }

    public Task<bool> IsPasswordConfiguredAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(IsPasswordConfigured || _configuredPassword != null);

    public Task SetPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        _configuredPassword = password;
        IsPasswordConfigured = true;
        return Task.CompletedTask;
    }

    public Task<bool> ValidatePasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        if (IsLockedOutResult)
        {
            return Task.FromResult(false);
        }

        if (_configuredPassword != null)
        {
            return Task.FromResult(password == _configuredPassword);
        }

        return Task.FromResult(true);
    }

    public Task<bool> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        _configuredPassword = newPassword;
        return Task.FromResult(true);
    }

    public bool IsLockedOut(out TimeSpan remainingLockout)
    {
        if (IsLockedOutResult)
        {
            remainingLockout = TimeSpan.FromMinutes(5);
            return true;
        }

        remainingLockout = TimeSpan.Zero;
        return false;
    }
}

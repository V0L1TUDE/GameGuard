namespace GameGuard.Core.Interfaces;

public interface IAdminAuthService
{
    Task<bool> IsPasswordConfiguredAsync(CancellationToken cancellationToken = default);
    Task SetPasswordAsync(string password, CancellationToken cancellationToken = default);
    Task<bool> ValidatePasswordAsync(string password, CancellationToken cancellationToken = default);
    Task<bool> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default);
    bool IsLockedOut(out TimeSpan remainingLockout);
}

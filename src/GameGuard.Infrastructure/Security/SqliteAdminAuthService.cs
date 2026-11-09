using System.Security.Cryptography;
using GameGuard.Core.Interfaces;
using GameGuard.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace GameGuard.Infrastructure.Security;

public sealed class SqliteAdminAuthService : IAdminAuthService
{
    private const string SettingKey = "AdminPasswordHash";
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 100_000;
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ISystemClock _systemClock;
    private readonly ILogger<SqliteAdminAuthService> _logger;

    private readonly object _lock = new();
    private int _failedAttempts;
    private DateTime? _lockoutUntilUtc;

    public SqliteAdminAuthService(
        SqliteConnectionFactory connectionFactory,
        ISystemClock systemClock,
        ILogger<SqliteAdminAuthService> logger)
    {
        _connectionFactory = connectionFactory;
        _systemClock = systemClock;
        _logger = logger;
    }

    public async Task<bool> IsPasswordConfiguredAsync(CancellationToken cancellationToken = default)
    {
        string? stored = await GetStoredHashAsync(cancellationToken).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(stored);
    }

    public async Task SetPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Admin password cannot be empty or whitespace.", nameof(password));
        }

        string hashString = HashPassword(password);
        await SaveStoredHashAsync(hashString, cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            _failedAttempts = 0;
            _lockoutUntilUtc = null;
        }

        _logger.LogInformation("Admin password configured successfully.");
    }

    public async Task<bool> ValidatePasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        DateTime nowUtc = _systemClock.UtcNow;

        lock (_lock)
        {
            if (_lockoutUntilUtc.HasValue && nowUtc < _lockoutUntilUtc.Value)
            {
                _logger.LogWarning("Admin authentication attempted while locked out.");
                return false;
            }
        }

        string? storedHash = await GetStoredHashAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            _logger.LogWarning("Admin password validation failed: no password has been configured.");
            return false;
        }

        bool isValid = VerifyPassword(password, storedHash);

        lock (_lock)
        {
            if (isValid)
            {
                _failedAttempts = 0;
                _lockoutUntilUtc = null;
                _logger.LogInformation("Admin password validated successfully.");
                return true;
            }

            _failedAttempts++;
            _logger.LogWarning("Failed admin password attempt ({AttemptCount}/{MaxAttempts}).", _failedAttempts, MaxFailedAttempts);

            if (_failedAttempts >= MaxFailedAttempts)
            {
                _lockoutUntilUtc = nowUtc.Add(LockoutDuration);
                _logger.LogCritical("Too many failed admin password attempts. Lockout enforced until {LockoutUntilUtc}.", _lockoutUntilUtc.Value);
            }

            return false;
        }
    }

    public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        if (!await ValidatePasswordAsync(currentPassword, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await SetPasswordAsync(newPassword, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public bool IsLockedOut(out TimeSpan remainingLockout)
    {
        lock (_lock)
        {
            DateTime nowUtc = _systemClock.UtcNow;
            if (_lockoutUntilUtc.HasValue && nowUtc < _lockoutUntilUtc.Value)
            {
                remainingLockout = _lockoutUntilUtc.Value - nowUtc;
                return true;
            }

            remainingLockout = TimeSpan.Zero;
            return false;
        }
    }

    private static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSizeBytes);

        return $"$pbkdf2-sha256$i={Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 5 || parts[1] != "pbkdf2-sha256")
        {
            return false;
        }

        if (!int.TryParse(parts[2].Replace("i=", ""), out int iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expectedHash = Convert.FromBase64String(parts[4]);
        }
        catch
        {
            return false;
        }

        byte[] computedHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
    }

    private async Task<string?> GetStoredHashAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", SettingKey);

        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar as string;
    }

    private async Task SaveStoredHashAsync(string hash, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            INSERT INTO AppSettings (Key, Value, UpdatedAtUtc)
            VALUES ($key, $val, $now)
            ON CONFLICT(Key) DO UPDATE SET Value = $val, UpdatedAtUtc = $now;
        ";
        command.Parameters.AddWithValue("$key", SettingKey);
        command.Parameters.AddWithValue("$val", hash);
        command.Parameters.AddWithValue("$now", _systemClock.UtcNow.ToString("o"));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

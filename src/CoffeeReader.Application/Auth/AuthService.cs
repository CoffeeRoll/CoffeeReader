using CoffeeReader.Application.Abstractions;
using CoffeeReader.Application.Common;
using CoffeeReader.Application.Users;
using CoffeeReader.Domain.Common;
using CoffeeReader.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CoffeeReader.Application.Auth;

public sealed class AuthService(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    TimeProvider clock)
{
    private static readonly SemaphoreSlim SetupLock = new(1, 1);
    private string? _dummyHash;

    public async Task<AuthStatusDto> GetStatusAsync(int? userId, CancellationToken cancellationToken)
    {
        var setupRequired = !await db.Users.AnyAsync(cancellationToken);
        UserDto? user = null;
        if (userId is { } id)
        {
            var entity = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            user = entity is null ? null : UserDto.From(entity);
        }

        return new AuthStatusDto(setupRequired, user);
    }

    /// <summary>
    /// Creates the first administrator. Only allowed while no accounts exist.
    /// </summary>
    public async Task<AuthenticatedUser> SetupAsync(SetupRequest request, CancellationToken cancellationToken)
    {
        PasswordPolicy.Validate(request.Password);
        await SetupLock.WaitAsync(cancellationToken);
        try
        {
            if (await db.Users.AnyAsync(cancellationToken))
            {
                throw new ConflictException("CoffeeReader has already been set up.");
            }

            var user = await UserService.CreateUserAsync(
                db, passwordHasher, clock, request.Username, request.DisplayName, request.Password, UserRole.Admin, cancellationToken);
            user.RecordLogin(clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(cancellationToken);
            return new AuthenticatedUser(UserDto.From(user), user.SecurityStamp);
        }
        finally
        {
            SetupLock.Release();
        }
    }

    /// <summary>
    /// Checks a username and password. Returns null when they don't match.
    /// </summary>
    public async Task<AuthenticatedUser?> SignInAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalized = TextNormalization.NormalizeKey(request.Username ?? string.Empty);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedUsername == normalized, cancellationToken);
        if (user is null)
        {
            // Spend the same time as a real check so usernames can't be probed by timing.
            _dummyHash ??= passwordHasher.Hash(Guid.NewGuid().ToString());
            passwordHasher.Verify(_dummyHash, request.Password ?? string.Empty);
            return null;
        }

        var result = passwordHasher.Verify(user.PasswordHash, request.Password ?? string.Empty);
        if (result == PasswordCheck.Failed)
        {
            return null;
        }

        if (result == PasswordCheck.SuccessRehashNeeded)
        {
            user.UpgradePasswordHash(passwordHasher.Hash(request.Password!));
        }

        user.RecordLogin(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
        return new AuthenticatedUser(UserDto.From(user), user.SecurityStamp);
    }

    public async Task<AuthenticatedUser> ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        PasswordPolicy.Validate(request.NewPassword, "newPassword");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new UnauthorizedException();

        if (passwordHasher.Verify(user.PasswordHash, request.CurrentPassword ?? string.Empty) == PasswordCheck.Failed)
        {
            throw ValidationException.For("currentPassword", "Your current password is incorrect.");
        }

        user.SetPasswordHash(passwordHasher.Hash(request.NewPassword));
        await db.SaveChangesAsync(cancellationToken);
        return new AuthenticatedUser(UserDto.From(user), user.SecurityStamp);
    }

    public async Task<UserSession?> GetSessionAsync(int userId, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserSession(u.Id, u.SecurityStamp, u.Role))
            .FirstOrDefaultAsync(cancellationToken);
}

/// <summary>
/// The fields needed to validate a session cookie.
/// </summary>
public sealed record UserSession(int UserId, string SecurityStamp, UserRole Role);

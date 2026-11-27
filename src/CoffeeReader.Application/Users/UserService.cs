using CoffeeReader.Application.Abstractions;
using CoffeeReader.Application.Common;
using CoffeeReader.Domain.Common;
using CoffeeReader.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CoffeeReader.Application.Users;

/// <summary>
/// Administrator-only management of user accounts.
/// </summary>
public sealed class UserService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IPasswordHasher passwordHasher,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken)
    {
        currentUser.RequireAdmin();
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(cancellationToken);
        return users.Select(UserDto.From).ToList();
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        currentUser.RequireAdmin();
        PasswordPolicy.Validate(request.Password);
        var user = await CreateUserAsync(db, passwordHasher, clock, request.Username, request.DisplayName, request.Password, request.Role, cancellationToken);
        return UserDto.From(user);
    }

    public async Task<UserDto> UpdateAsync(int id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        currentUser.RequireAdmin();
        var user = await FindAsync(id, cancellationToken);

        if (request.DisplayName is not null)
        {
            Apply(() => user.Rename(request.DisplayName));
        }

        if (request.Role is { } role && role != user.Role)
        {
            if (user.IsAdmin && await IsLastAdminAsync(user, cancellationToken))
            {
                throw new ConflictException("There must always be at least one administrator.");
            }

            user.ChangeRole(role);
        }

        await db.SaveChangesAsync(cancellationToken);
        return UserDto.From(user);
    }

    public async Task ResetPasswordAsync(int id, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        currentUser.RequireAdmin();
        PasswordPolicy.Validate(request.NewPassword, "newPassword");
        var user = await FindAsync(id, cancellationToken);
        user.SetPasswordHash(passwordHasher.Hash(request.NewPassword));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        currentUser.RequireAdmin();
        if (id == currentUser.RequireUserId())
        {
            throw new ConflictException("You can't delete your own account.");
        }

        var user = await FindAsync(id, cancellationToken);
        if (user.IsAdmin && await IsLastAdminAsync(user, cancellationToken))
        {
            throw new ConflictException("There must always be at least one administrator.");
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);
    }

    internal static async Task<User> CreateUserAsync(
        IApplicationDbContext db,
        IPasswordHasher passwordHasher,
        TimeProvider clock,
        string username,
        string? displayName,
        string password,
        UserRole role,
        CancellationToken cancellationToken)
    {
        var user = Apply(() => new User(
            username ?? string.Empty,
            string.IsNullOrWhiteSpace(displayName) ? username ?? string.Empty : displayName,
            passwordHasher.Hash(password),
            role,
            clock.GetUtcNow().UtcDateTime));

        if (await db.Users.AnyAsync(u => u.NormalizedUsername == user.NormalizedUsername, cancellationToken))
        {
            throw new ConflictException($"The username '{user.Username}' is already taken.");
        }

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    private async Task<bool> IsLastAdminAsync(User user, CancellationToken cancellationToken) =>
        !await db.Users.AnyAsync(u => u.Id != user.Id && u.Role == UserRole.Admin, cancellationToken);

    private async Task<User> FindAsync(int id, CancellationToken cancellationToken) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
        ?? throw NotFoundException.For("User", id);

    private static T Apply<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (DomainException ex)
        {
            throw new ValidationException(ex.Message);
        }
    }

    private static void Apply(Action action) => Apply(() =>
    {
        action();
        return true;
    });
}

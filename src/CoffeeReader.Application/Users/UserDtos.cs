using CoffeeReader.Domain.Entities;

namespace CoffeeReader.Application.Users;

public sealed record UserDto(int Id, string Username, string DisplayName, UserRole Role, DateTime CreatedAt, DateTime? LastLoginAt)
{
    public static UserDto From(User user) =>
        new(user.Id, user.Username, user.DisplayName, user.Role, user.CreatedAt, user.LastLoginAt);
}

public sealed record CreateUserRequest(string Username, string? DisplayName, string Password, UserRole Role);

public sealed record UpdateUserRequest(string? DisplayName, UserRole? Role);

public sealed record ResetPasswordRequest(string NewPassword);

using CoffeeReader.Application.Common;

namespace CoffeeReader.Application.Abstractions;

/// <summary>
/// The user making the current request.
/// </summary>
public interface ICurrentUser
{
    int? UserId { get; }

    bool IsAdmin { get; }
}

public static class CurrentUserExtensions
{
    public static int RequireUserId(this ICurrentUser user) =>
        user.UserId ?? throw new UnauthorizedException();

    public static void RequireAdmin(this ICurrentUser user)
    {
        user.RequireUserId();
        if (!user.IsAdmin)
        {
            throw new ForbiddenException("This action requires an administrator.");
        }
    }
}

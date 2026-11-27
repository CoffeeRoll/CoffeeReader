using System.Security.Claims;
using CoffeeReader.Application.Auth;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace CoffeeReader.Api.Auth;

internal static class SessionClaims
{
    public const string SecurityStamp = "coffeereader:stamp";
    public const string AdminRole = "Admin";
    public const string AdminPolicy = "Admin";

    public static ClaimsPrincipal CreatePrincipal(int userId, string username, string role, string securityStamp)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role),
                new Claim(SecurityStamp, securityStamp),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    public static ClaimsPrincipal CreatePrincipal(AuthenticatedUser user) =>
        CreatePrincipal(user.User.Id, user.User.Username, user.User.Role.ToString(), user.SecurityStamp);

    public static int? GetUserId(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

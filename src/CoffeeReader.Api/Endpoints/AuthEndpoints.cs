using CoffeeReader.Api.Auth;
using CoffeeReader.Application.Abstractions;
using CoffeeReader.Application.Auth;
using CoffeeReader.Application.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CoffeeReader.Api.Endpoints;

internal static class AuthEndpoints
{
    public const string LoginRateLimit = "login";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth").WithTags("Auth");

        group.MapGet("/status", async (HttpContext http, AuthService auth, CancellationToken ct) =>
            await auth.GetStatusAsync(http.User.GetUserId(), ct)).AllowAnonymous();

        group.MapPost("/setup", async (SetupRequest request, HttpContext http, AuthService auth, CancellationToken ct) =>
        {
            var user = await auth.SetupAsync(request, ct);
            await SignInAsync(http, user, rememberMe: true);
            return user.User;
        }).AllowAnonymous().RequireRateLimiting(LoginRateLimit);

        group.MapPost("/login", async Task<Results<Ok<UserDto>, ProblemHttpResult>> (LoginRequest request, HttpContext http, AuthService auth, CancellationToken ct) =>
        {
            var user = await auth.SignInAsync(request, ct);
            if (user is null)
            {
                return TypedResults.Problem(title: "Sign in failed", detail: "That username and password don't match.", statusCode: StatusCodes.Status401Unauthorized);
            }

            await SignInAsync(http, user, request.RememberMe);
            return TypedResults.Ok(user.User);
        }).AllowAnonymous().RequireRateLimiting(LoginRateLimit);

        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).AllowAnonymous();

        group.MapGet("/me", async Task<Results<Ok<UserDto>, UnauthorizedHttpResult>> (ICurrentUser currentUser, AuthService auth, CancellationToken ct) =>
        {
            var status = await auth.GetStatusAsync(currentUser.RequireUserId(), ct);
            return status.User is null ? TypedResults.Unauthorized() : TypedResults.Ok(status.User);
        }).RequireAuthorization();

        group.MapPost("/change-password", async (ChangePasswordRequest request, HttpContext http, ICurrentUser currentUser, AuthService auth, SessionCache sessions, CancellationToken ct) =>
        {
            var userId = currentUser.RequireUserId();
            var user = await auth.ChangePasswordAsync(userId, request, ct);
            sessions.Invalidate(userId);

            // Other devices are signed out by the new security stamp; keep this one signed in.
            await SignInAsync(http, user, rememberMe: true);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static Task SignInAsync(HttpContext http, AuthenticatedUser user, bool rememberMe) =>
        http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            SessionClaims.CreatePrincipal(user),
            new AuthenticationProperties { IsPersistent = rememberMe, AllowRefresh = true });
}

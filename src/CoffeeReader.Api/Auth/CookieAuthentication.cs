using System.Security.Claims;
using CoffeeReader.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace CoffeeReader.Api.Auth;

internal static class CookieAuthentication
{
    public static IServiceCollection AddCookieSessions(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<SessionCache>();
        services.AddHttpContextAccessor();
        services.AddScoped<Application.Abstractions.ICurrentUser, HttpCurrentUser>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "coffeereader.session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;

                // This is an API: answer with status codes instead of redirecting to a login page.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                options.Events.OnValidatePrincipal = ValidateSessionAsync;
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(SessionClaims.AdminPolicy, policy => policy.RequireRole(SessionClaims.AdminRole));

        return services;
    }

    /// <summary>
    /// Ends sessions for deleted users or after a password change, and picks up role changes.
    /// </summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var userId = principal?.GetUserId();
        var stamp = principal?.FindFirstValue(SessionClaims.SecurityStamp);
        if (principal is null || userId is null || stamp is null)
        {
            await RejectAsync(context);
            return;
        }

        var services = context.HttpContext.RequestServices;
        var session = await services.GetRequiredService<SessionCache>()
            .GetAsync(userId.Value, services.GetRequiredService<AuthService>(), context.HttpContext.RequestAborted);
        if (session is null || !string.Equals(session.SecurityStamp, stamp, StringComparison.Ordinal))
        {
            await RejectAsync(context);
            return;
        }

        var role = session.Role.ToString();
        if (!principal.IsInRole(role))
        {
            context.ReplacePrincipal(SessionClaims.CreatePrincipal(userId.Value, principal.Identity?.Name ?? string.Empty, role, stamp));
            context.ShouldRenew = true;
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : Application.Abstractions.ICurrentUser
{
    public int? UserId => accessor.HttpContext?.User.GetUserId();

    public bool IsAdmin => accessor.HttpContext?.User.IsInRole(SessionClaims.AdminRole) == true;
}

using CoffeeReader.Api.Auth;
using CoffeeReader.Application.Users;

namespace CoffeeReader.Api.Endpoints;

internal static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users").WithTags("Users").RequireAuthorization(SessionClaims.AdminPolicy);

        group.MapGet("/", (UserService users, CancellationToken ct) => users.ListAsync(ct));

        group.MapPost("/", async (CreateUserRequest request, UserService users, CancellationToken ct) =>
        {
            var user = await users.CreateAsync(request, ct);
            return Results.Created($"/api/users/{user.Id}", user);
        });

        group.MapPut("/{id:int}", async (int id, UpdateUserRequest request, UserService users, SessionCache sessions, CancellationToken ct) =>
        {
            var user = await users.UpdateAsync(id, request, ct);
            sessions.Invalidate(id);
            return user;
        });

        group.MapPost("/{id:int}/reset-password", async (int id, ResetPasswordRequest request, UserService users, SessionCache sessions, CancellationToken ct) =>
        {
            await users.ResetPasswordAsync(id, request, ct);
            sessions.Invalidate(id);
            return Results.NoContent();
        });

        group.MapDelete("/{id:int}", async (int id, UserService users, SessionCache sessions, CancellationToken ct) =>
        {
            await users.DeleteAsync(id, ct);
            sessions.Invalidate(id);
            return Results.NoContent();
        });
    }
}

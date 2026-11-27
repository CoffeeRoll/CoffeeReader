using CoffeeReader.Api.Auth;
using CoffeeReader.Application.Library;

namespace CoffeeReader.Api.Endpoints;

internal static class LibraryEndpoints
{
    public static void MapLibraryEndpoints(this IEndpointRouteBuilder routes, long maxUploadBytes)
    {
        var group = routes.MapGroup("/api/library").WithTags("Library").RequireAuthorization();

        group.MapGet("/status", (LibraryService library, CancellationToken ct) => library.GetStatusAsync(ct));

        group.MapGet("/formats", (LibraryService library) => new
        {
            extensions = library.SupportedExtensions,
            maxUploadBytes,
        });

        group.MapPost("/scan", (LibraryService library) =>
        {
            library.RequestScan();
            return Results.Accepted();
        }).RequireAuthorization(SessionClaims.AdminPolicy);

        group.MapDelete("/missing", async (LibraryService library, CancellationToken ct) =>
            Results.Ok(new { removed = await library.RemoveMissingAsync(ct) }))
            .RequireAuthorization(SessionClaims.AdminPolicy);
    }
}

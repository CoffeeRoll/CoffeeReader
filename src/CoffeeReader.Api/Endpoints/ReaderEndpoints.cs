using CoffeeReader.Api.Middleware;
using CoffeeReader.Application.Reading;

namespace CoffeeReader.Api.Endpoints;

internal static class ReaderEndpoints
{
    public static void MapReaderEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/books/{id:int}").WithTags("Reader").RequireAuthorization();

        group.MapGet("/manifest", (int id, ReaderService reader, CancellationToken ct) => reader.GetManifestAsync(id, ct));

        group.MapGet("/chapters/{index:int}", async (int id, int index, HttpContext http, ReaderService reader, CancellationToken ct) =>
        {
            var chapter = await reader.GetChapterAsync(id, index, ct);
            BookEndpoints.SetPrivateCache(http.Response, TimeSpan.FromMinutes(10));
            return chapter;
        });

        group.MapGet("/files/{**path}", async (int id, string path, HttpContext http, ReaderService reader, CancellationToken ct) =>
        {
            var resource = await reader.GetResourceAsync(id, path, ct);
            BookEndpoints.SetPrivateCache(http.Response, TimeSpan.FromDays(1));
            http.Response.Headers.ContentSecurityPolicy = SecurityHeaders.BookFilePolicy;
            return Results.Bytes(resource.Content, resource.ContentType);
        });

        group.MapGet("/search", (int id, string? q, ReaderService reader, CancellationToken ct) => reader.SearchAsync(id, q, ct));

        group.MapGet("/progress", async (int id, ProgressService progress, CancellationToken ct) =>
            await progress.GetAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NoContent());

        group.MapPut("/progress", (int id, SaveProgressRequest request, ProgressService progress, CancellationToken ct) =>
            progress.SaveAsync(id, request, ct));

        group.MapPut("/progress/finished", async (int id, SetFinishedRequest request, ProgressService progress, CancellationToken ct) =>
            await progress.SetFinishedAsync(id, request.Finished, ct) is { } dto ? Results.Ok(dto) : Results.NoContent());
    }
}

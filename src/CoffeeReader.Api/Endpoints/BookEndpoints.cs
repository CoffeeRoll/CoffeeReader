using CoffeeReader.Api.Auth;
using CoffeeReader.Application.Books;
using CoffeeReader.Application.Library;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace CoffeeReader.Api.Endpoints;

internal static class BookEndpoints
{
    public static void MapBookEndpoints(this IEndpointRouteBuilder routes, long maxUploadBytes)
    {
        var group = routes.MapGroup("/api/books").WithTags("Books").RequireAuthorization();

        group.MapGet("/", (BookService books, CancellationToken ct) => books.ListAsync(ct));

        group.MapGet("/{id:int}", (int id, BookService books, CancellationToken ct) => books.GetAsync(id, ct));

        group.MapPut("/{id:int}", (int id, UpdateBookRequest request, BookService books, CancellationToken ct) => books.UpdateAsync(id, request, ct))
            .RequireAuthorization(SessionClaims.AdminPolicy);

        group.MapDelete("/{id:int}", async (int id, BookService books, CancellationToken ct) =>
        {
            await books.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(SessionClaims.AdminPolicy);

        group.MapGet("/{id:int}/cover", (int id, string? v, HttpContext http, BookService books) =>
        {
            var cover = books.OpenCover(id);
            if (cover is null)
            {
                return Results.NotFound();
            }

            // Cover URLs carry a version, so they can be cached until the book changes.
            http.Response.Headers.CacheControl = v is null ? "private, no-cache" : "private, max-age=31536000, immutable";
            return Results.Stream(cover.Content, cover.ContentType, lastModified: cover.LastModified);
        });

        group.MapGet("/{id:int}/download", async (int id, BookService books, CancellationToken ct) =>
        {
            var download = await books.GetDownloadAsync(id, ct);
            return Results.File(download.FullPath, download.ContentType, download.FileName, enableRangeProcessing: true);
        });

        // The book file itself, inline and with range support, for readers that render it in the
        // browser (PDF.js fetches only the byte ranges it needs).
        group.MapGet("/{id:int}/file", async (int id, HttpContext http, BookService books, CancellationToken ct) =>
        {
            var file = await books.GetDownloadAsync(id, ct);
            SetPrivateCache(http.Response, TimeSpan.FromDays(1));
            http.Response.Headers.ContentSecurityPolicy = Middleware.SecurityHeaders.BookFilePolicy;
            return Results.File(
                file.FullPath,
                file.ContentType,
                lastModified: file.LastModified,
                entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{file.Hash}\""),
                enableRangeProcessing: true);
        });

        // One book per request keeps uploads resumable from the UI and lets it show per-file progress.
        group.MapPost("/upload", async (HttpRequest request, LibraryService library, CancellationToken ct) =>
        {
            var sizeLimit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeLimit is { IsReadOnly: false })
            {
                sizeLimit.MaxRequestBodySize = maxUploadBytes;
            }

            if (!request.HasFormContentType)
            {
                return Results.Problem(title: "Bad request", detail: "Send the books as multipart form data.", statusCode: 400);
            }

            var form = await request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = maxUploadBytes }, ct);
            if (form.Files.Count == 0)
            {
                return Results.Problem(title: "Bad request", detail: "Choose at least one file to upload.", statusCode: 400);
            }

            var results = new List<UploadResultDto>();
            foreach (var file in form.Files)
            {
                await using var stream = file.OpenReadStream();
                results.Add(await library.UploadAsync(stream, file.FileName, ct));
            }

            return Results.Ok(results);
        });
    }

    internal static void SetPrivateCache(HttpResponse response, TimeSpan maxAge) =>
        response.Headers[HeaderNames.CacheControl] = $"private, max-age={(int)maxAge.TotalSeconds}";
}

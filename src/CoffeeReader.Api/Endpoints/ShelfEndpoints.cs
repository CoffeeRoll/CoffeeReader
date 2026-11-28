using CoffeeReader.Application.Shelves;

namespace CoffeeReader.Api.Endpoints;

internal static class ShelfEndpoints
{
    public static void MapShelfEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/shelves").WithTags("Shelves").RequireAuthorization();

        group.MapGet("/", (ShelfService shelves, CancellationToken ct) => shelves.ListAsync(ct));

        group.MapPost("/", async (CreateShelfRequest request, ShelfService shelves, CancellationToken ct) =>
        {
            var shelf = await shelves.CreateAsync(request, ct);
            return Results.Created($"/api/shelves/{shelf.Id}", shelf);
        });

        group.MapPut("/order", (ReorderShelvesRequest request, ShelfService shelves, CancellationToken ct) => shelves.ReorderAsync(request, ct));

        group.MapPost("/move", (MoveBookRequest request, ShelfService shelves, CancellationToken ct) => shelves.MoveBookAsync(request, ct));

        group.MapPut("/{id:int}", (int id, RenameShelfRequest request, ShelfService shelves, CancellationToken ct) => shelves.RenameAsync(id, request, ct));

        group.MapDelete("/{id:int}", async (int id, ShelfService shelves, CancellationToken ct) =>
        {
            await shelves.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPut("/{id:int}/books", (int id, SetShelfBooksRequest request, ShelfService shelves, CancellationToken ct) => shelves.SetBooksAsync(id, request, ct));

        group.MapPost("/{id:int}/books", (int id, AddShelfBookRequest request, ShelfService shelves, CancellationToken ct) => shelves.AddBookAsync(id, request, ct));

        group.MapDelete("/{id:int}/books/{bookId:int}", (int id, int bookId, ShelfService shelves, CancellationToken ct) => shelves.RemoveBookAsync(id, bookId, ct));
    }
}

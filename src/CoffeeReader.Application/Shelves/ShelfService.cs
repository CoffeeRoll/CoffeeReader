using CoffeeReader.Application.Abstractions;
using CoffeeReader.Application.Common;
using CoffeeReader.Domain.Common;
using CoffeeReader.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CoffeeReader.Application.Shelves;

/// <summary>
/// Each user's personal shelves. Users can only see and change their own.
/// </summary>
public sealed class ShelfService(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public const int MaxShelvesPerUser = 200;

    public async Task<IReadOnlyList<ShelfDto>> ListAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var shelves = await db.Shelves.AsNoTracking()
            .Include(s => s.Books)
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);
        return shelves.Select(ToDto).ToList();
    }

    public async Task<ShelfDto> CreateAsync(CreateShelfRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var count = await db.Shelves.CountAsync(s => s.UserId == userId, cancellationToken);
        if (count >= MaxShelvesPerUser)
        {
            throw new ConflictException($"You can have at most {MaxShelvesPerUser} shelves.");
        }

        var nextOrder = count == 0 ? 0 : await db.Shelves.Where(s => s.UserId == userId).MaxAsync(s => s.SortOrder, cancellationToken) + 1;
        var shelf = Guard(() => new Shelf(userId, request.Name ?? string.Empty, nextOrder, Now));
        if (request.BookIds is { Count: > 0 })
        {
            await EnsureBooksExistAsync(request.BookIds, cancellationToken);
            shelf.Arrange(request.BookIds, Now);
        }

        db.Shelves.Add(shelf);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(shelf);
    }

    public async Task<ShelfDto> RenameAsync(int id, RenameShelfRequest request, CancellationToken cancellationToken)
    {
        var shelf = await FindAsync(id, cancellationToken);
        Guard(() => shelf.Rename(request.Name ?? string.Empty));
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(shelf);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var shelf = await FindAsync(id, cancellationToken);
        db.Shelves.Remove(shelf);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShelfDto>> ReorderAsync(ReorderShelvesRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var shelves = await db.Shelves.Where(s => s.UserId == userId).ToListAsync(cancellationToken);
        var requested = (request.ShelfIds ?? []).Distinct().ToList();
        if (requested.Count != shelves.Count || shelves.Any(s => !requested.Contains(s.Id)))
        {
            throw ValidationException.For("shelfIds", "Send every one of your shelves, in the new order.");
        }

        foreach (var shelf in shelves)
        {
            shelf.MoveTo(requested.IndexOf(shelf.Id));
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ListAsync(cancellationToken);
    }

    public async Task<ShelfDto> SetBooksAsync(int id, SetShelfBooksRequest request, CancellationToken cancellationToken)
    {
        var shelf = await FindAsync(id, cancellationToken);
        var bookIds = (request.BookIds ?? []).Distinct().ToList();
        await EnsureBooksExistAsync(bookIds, cancellationToken);
        shelf.Arrange(bookIds, Now);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(shelf);
    }

    public async Task<ShelfDto> AddBookAsync(int id, AddShelfBookRequest request, CancellationToken cancellationToken)
    {
        var shelf = await FindAsync(id, cancellationToken);
        await EnsureBooksExistAsync([request.BookId], cancellationToken);
        shelf.Insert(request.BookId, request.Position, Now);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(shelf);
    }

    public async Task<ShelfDto> RemoveBookAsync(int id, int bookId, CancellationToken cancellationToken)
    {
        var shelf = await FindAsync(id, cancellationToken);
        shelf.Remove(bookId);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(shelf);
    }

    public async Task<IReadOnlyList<ShelfDto>> MoveBookAsync(MoveBookRequest request, CancellationToken cancellationToken)
    {
        if (request.FromShelfId is null && request.ToShelfId is null)
        {
            throw ValidationException.For("toShelfId", "Say which shelf the book is moving to or from.");
        }

        await EnsureBooksExistAsync([request.BookId], cancellationToken);
        if (request.FromShelfId is { } fromId && fromId != request.ToShelfId)
        {
            (await FindAsync(fromId, cancellationToken)).Remove(request.BookId);
        }

        if (request.ToShelfId is { } toId)
        {
            (await FindAsync(toId, cancellationToken)).Insert(request.BookId, request.Position, Now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ListAsync(cancellationToken);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<Shelf> FindAsync(int id, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        return await db.Shelves.Include(s => s.Books)
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, cancellationToken)
            ?? throw NotFoundException.For("Shelf", id);
    }

    private async Task EnsureBooksExistAsync(IReadOnlyCollection<int> bookIds, CancellationToken cancellationToken)
    {
        if (bookIds.Count == 0)
        {
            return;
        }

        var found = await db.Books.CountAsync(b => bookIds.Contains(b.Id), cancellationToken);
        if (found != bookIds.Distinct().Count())
        {
            throw ValidationException.For("bookIds", "One or more of those books don't exist.");
        }
    }

    private static ShelfDto ToDto(Shelf shelf) => new(shelf.Id, shelf.Name, shelf.SortOrder, shelf.OrderedBookIds.ToList());

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (DomainException ex)
        {
            throw ValidationException.For("name", ex.Message);
        }
    }

    private static void Guard(Action action) => Guard(() =>
    {
        action();
        return 0;
    });
}

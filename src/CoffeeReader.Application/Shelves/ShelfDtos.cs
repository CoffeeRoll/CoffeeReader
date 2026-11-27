namespace CoffeeReader.Application.Shelves;

public sealed record ShelfDto(int Id, string Name, int SortOrder, IReadOnlyList<int> BookIds);

public sealed record CreateShelfRequest(string Name, IReadOnlyList<int>? BookIds = null);

public sealed record RenameShelfRequest(string Name);

public sealed record ReorderShelvesRequest(IReadOnlyList<int> ShelfIds);

public sealed record SetShelfBooksRequest(IReadOnlyList<int> BookIds);

public sealed record AddShelfBookRequest(int BookId, int? Position);

/// <summary>
/// Moves a book between shelves in one step. A null source means "add"; a null target means "remove".
/// </summary>
public sealed record MoveBookRequest(int BookId, int? FromShelfId, int? ToShelfId, int? Position);

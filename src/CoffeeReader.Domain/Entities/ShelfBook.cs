namespace CoffeeReader.Domain.Entities;

public sealed class ShelfBook
{
    private ShelfBook()
    {
    }

    internal ShelfBook(int bookId, int sortOrder, DateTime addedAt)
    {
        BookId = bookId;
        SortOrder = sortOrder;
        AddedAt = addedAt;
    }

    public int ShelfId { get; private set; }

    public int BookId { get; private set; }

    public Book Book { get; private set; } = null!;

    public int SortOrder { get; internal set; }

    public DateTime AddedAt { get; private set; }
}

using CoffeeReader.Domain.Common;

namespace CoffeeReader.Domain.Entities;

/// <summary>
/// A user-curated, ordered group of books. Each user owns their own shelves.
/// </summary>
public sealed class Shelf
{
    public const int MaxNameLength = 100;

    private readonly List<ShelfBook> _books = [];

    private Shelf()
    {
    }

    public Shelf(int userId, string name, int sortOrder, DateTime createdAt)
    {
        UserId = userId;
        Rename(name);
        SortOrder = sortOrder;
        CreatedAt = createdAt;
    }

    public int Id { get; private set; }

    public int UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public IReadOnlyList<ShelfBook> Books => _books;

    public IEnumerable<int> OrderedBookIds => _books.OrderBy(b => b.SortOrder).Select(b => b.BookId);

    public void Rename(string name)
    {
        var trimmed = TextNormalization.CollapseWhitespace(name).Trim();
        if (trimmed.Length is 0 or > MaxNameLength)
        {
            throw new DomainException($"Shelf names must be between 1 and {MaxNameLength} characters.");
        }

        Name = trimmed;
    }

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;

    public bool Contains(int bookId) => _books.Any(b => b.BookId == bookId);

    /// <summary>
    /// Inserts a book at <paramref name="position"/> (or the end). If the book is already on the shelf it is moved.
    /// </summary>
    public void Insert(int bookId, int? position, DateTime when)
    {
        var ordered = OrderedBookIds.Where(id => id != bookId).ToList();
        var index = Math.Clamp(position ?? ordered.Count, 0, ordered.Count);
        ordered.Insert(index, bookId);
        Arrange(ordered, when);
    }

    public bool Remove(int bookId)
    {
        var existing = _books.FirstOrDefault(b => b.BookId == bookId);
        if (existing is null)
        {
            return false;
        }

        _books.Remove(existing);
        Renumber();
        return true;
    }

    /// <summary>
    /// Replaces the shelf contents with exactly <paramref name="bookIds"/>, in that order.
    /// </summary>
    public void Arrange(IReadOnlyList<int> bookIds, DateTime when)
    {
        var wanted = bookIds.Distinct().ToList();
        _books.RemoveAll(b => !wanted.Contains(b.BookId));
        for (var i = 0; i < wanted.Count; i++)
        {
            var existing = _books.FirstOrDefault(b => b.BookId == wanted[i]);
            if (existing is null)
            {
                _books.Add(new ShelfBook(wanted[i], i, when));
            }
            else
            {
                existing.SortOrder = i;
            }
        }
    }

    private void Renumber()
    {
        var i = 0;
        foreach (var entry in _books.OrderBy(b => b.SortOrder))
        {
            entry.SortOrder = i++;
        }
    }
}

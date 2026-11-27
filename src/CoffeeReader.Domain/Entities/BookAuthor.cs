namespace CoffeeReader.Domain.Entities;

public sealed class BookAuthor
{
    private BookAuthor()
    {
    }

    public BookAuthor(Author author, int order)
    {
        Author = author;
        AuthorId = author.Id;
        Order = order;
    }

    public int BookId { get; private set; }

    public int AuthorId { get; private set; }

    public Author Author { get; private set; } = null!;

    public int Order { get; private set; }
}

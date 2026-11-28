using CoffeeReader.Domain.Common;
using CoffeeReader.Domain.Entities;

namespace CoffeeReader.Tests.Domain;

public class ShelfTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Insert_places_book_at_position_and_moves_existing_ones()
    {
        var shelf = new Shelf(1, "Fantasy", 0, Now);
        shelf.Arrange([10, 20, 30], Now);

        shelf.Insert(40, 1, Now);
        Assert.Equal([10, 40, 20, 30], shelf.OrderedBookIds);

        shelf.Insert(10, 3, Now);
        Assert.Equal([40, 20, 30, 10], shelf.OrderedBookIds);
    }

    [Fact]
    public void Insert_without_position_appends()
    {
        var shelf = new Shelf(1, "To read", 0, Now);
        shelf.Insert(1, null, Now);
        shelf.Insert(2, null, Now);
        Assert.Equal([1, 2], shelf.OrderedBookIds);
    }

    [Fact]
    public void Remove_renumbers_remaining_books()
    {
        var shelf = new Shelf(1, "Sci-fi", 0, Now);
        shelf.Arrange([1, 2, 3], Now);
        Assert.True(shelf.Remove(2));
        Assert.False(shelf.Remove(99));
        Assert.Equal([1, 3], shelf.OrderedBookIds);
        Assert.Equal([0, 1], shelf.Books.OrderBy(b => b.SortOrder).Select(b => b.SortOrder));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Names_are_required(string name) =>
        Assert.Throws<DomainException>(() => new Shelf(1, name, 0, Now));
}

public class BookTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Percentage_is_weighted_by_chapter_length()
    {
        var book = new Book(new BookFile("a.epub", 1, Now, "hash", BookFormat.Epub), Now);
        book.ReplaceStructure(
            [new BookChapter(0, "a", null, 100, true), new BookChapter(1, "b", null, 300, true)],
            [],
            Now);

        Assert.Equal(0, book.CalculatePercentage(0, 0));
        Assert.Equal(0.125, book.CalculatePercentage(0, 0.5));
        Assert.Equal(0.25, book.CalculatePercentage(1, 0));
        Assert.Equal(1, book.CalculatePercentage(1, 1));
    }

    [Theory]
    [InlineData("The Hobbit", "Hobbit, The")]
    [InlineData("A Wizard of Earthsea", "Wizard of Earthsea, A")]
    [InlineData("Dune", "Dune")]
    public void Sort_titles_ignore_leading_articles(string title, string expected) =>
        Assert.Equal(expected, TextNormalization.ToSortTitle(title));

    [Fact]
    public void Progress_marks_finished_near_the_end()
    {
        var progress = new ReadingProgress(1, 1, Now);
        progress.Update(3, 0.5, 0.5, Now);
        Assert.False(progress.IsFinished);
        progress.Update(9, 1, 0.999, Now);
        Assert.True(progress.IsFinished);
        Assert.Equal(Now, progress.FinishedAt);
    }
}

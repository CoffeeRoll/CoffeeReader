using CoffeeReader.Infrastructure.Ebooks;

namespace CoffeeReader.Tests.Ebooks;

public class AuthorNamesTests
{
    [Theory]
    [InlineData("Black & Decker")]
    [InlineData("Simon & Schuster")]
    [InlineData("AT&T")]
    [InlineData("Strunk & White")]
    [InlineData("Jane & John Smith")]
    [InlineData("John Wiley & Sons, Inc.")]
    [InlineData("Ernst & Young Global Ltd")]
    public void Keeps_companies_and_pairs_whole(string value) =>
        Assert.Equal([value], AuthorNames.Split(value));

    [Theory]
    [InlineData("Jane Smith & John Doe", new[] { "Jane Smith", "John Doe" })]
    [InlineData("J. R. R. Tolkien & Christopher Tolkien", new[] { "J. R. R. Tolkien", "Christopher Tolkien" })]
    [InlineData("Smith, Jane & Doe, John", new[] { "Smith, Jane", "Doe, John" })]
    [InlineData("Ines Calloway; Mark Ode", new[] { "Ines Calloway", "Mark Ode" })]
    [InlineData("Black & Decker; Jane  Smith ", new[] { "Black & Decker", "Jane Smith" })]
    public void Splits_lists_of_people(string value, string[] expected) =>
        Assert.Equal(expected, AuthorNames.Split(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ; ")]
    public void Returns_nothing_for_empty_values(string? value) =>
        Assert.Empty(AuthorNames.Split(value));
}

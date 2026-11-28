using CoffeeReader.Domain.Common;

namespace CoffeeReader.Domain.Entities;

public sealed class Author
{
    private Author()
    {
    }

    public Author(string name, string? sortName = null)
    {
        Name = TextNormalization.CollapseWhitespace(name).Trim();
        NormalizedName = TextNormalization.NormalizeKey(Name);
        SortName = string.IsNullOrWhiteSpace(sortName) ? TextNormalization.ToAuthorSortName(Name) : sortName.Trim();
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public string SortName { get; private set; } = string.Empty;
}

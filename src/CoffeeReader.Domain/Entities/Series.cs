using CoffeeReader.Domain.Common;

namespace CoffeeReader.Domain.Entities;

public sealed class Series
{
    private Series()
    {
    }

    public Series(string name)
    {
        Name = TextNormalization.CollapseWhitespace(name).Trim();
        NormalizedName = TextNormalization.NormalizeKey(Name);
        SortName = TextNormalization.ToSortTitle(Name);
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public string SortName { get; private set; } = string.Empty;
}

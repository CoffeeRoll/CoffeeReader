using CoffeeReader.Application.Abstractions;
using CoffeeReader.Domain.Common;
using CoffeeReader.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CoffeeReader.Application.Books;

/// <summary>
/// Finds or creates the shared author and series records a book points at.
/// </summary>
internal sealed class CatalogResolver(IApplicationDbContext db)
{
    public async Task<List<Author>> ResolveAuthorsAsync(IEnumerable<(string Name, string? SortName)> names, CancellationToken cancellationToken)
    {
        var result = new List<Author>();
        foreach (var (name, sortName) in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var key = TextNormalization.NormalizeKey(name);
            if (result.Any(a => a.NormalizedName == key))
            {
                continue;
            }

            var author = db.Authors.Local.FirstOrDefault(a => a.NormalizedName == key)
                ?? await db.Authors.FirstOrDefaultAsync(a => a.NormalizedName == key, cancellationToken);
            if (author is null)
            {
                author = new Author(name, sortName);
                db.Authors.Add(author);
            }

            result.Add(author);
        }

        return result;
    }

    public async Task<Series?> ResolveSeriesAsync(string? name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var key = TextNormalization.NormalizeKey(name);
        var series = db.Series.Local.FirstOrDefault(s => s.NormalizedName == key)
            ?? await db.Series.FirstOrDefaultAsync(s => s.NormalizedName == key, cancellationToken);
        if (series is null)
        {
            series = new Series(name);
            db.Series.Add(series);
        }

        return series;
    }

    /// <summary>Deletes authors and series that no book refers to any more.</summary>
    public async Task RemoveOrphansAsync(CancellationToken cancellationToken)
    {
        await db.Authors
            .Where(a => !db.Books.Any(b => b.Authors.Any(ba => ba.AuthorId == a.Id)))
            .ExecuteDeleteAsync(cancellationToken);
        await db.Series
            .Where(s => !db.Books.Any(b => b.SeriesId == s.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}

using CoffeeReader.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CoffeeReader.Application.Abstractions;

/// <summary>
/// The unit of work used by application services. Implemented by the persistence layer.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }

    DbSet<Book> Books { get; }

    DbSet<Author> Authors { get; }

    DbSet<Series> Series { get; }

    DbSet<ReadingProgress> ReadingProgress { get; }

    DbSet<Shelf> Shelves { get; }

    DbSet<ShelfBook> ShelfBooks { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

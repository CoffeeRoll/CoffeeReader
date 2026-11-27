using CoffeeReader.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeReader.Infrastructure.Persistence.Configurations;

internal sealed class ReadingProgressConfiguration : IEntityTypeConfiguration<ReadingProgress>
{
    public void Configure(EntityTypeBuilder<ReadingProgress> builder)
    {
        builder.ToTable("ReadingProgress");
        builder.HasIndex(p => new { p.UserId, p.BookId }).IsUnique();
        builder.HasIndex(p => p.BookId);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Book>().WithMany().HasForeignKey(p => p.BookId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ShelfConfiguration : IEntityTypeConfiguration<Shelf>
{
    public void Configure(EntityTypeBuilder<Shelf> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(Shelf.MaxNameLength).IsRequired();
        builder.HasIndex(s => new { s.UserId, s.SortOrder });
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(s => s.Books).WithOne().HasForeignKey(b => b.ShelfId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Books).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(s => s.OrderedBookIds);
    }
}

internal sealed class ShelfBookConfiguration : IEntityTypeConfiguration<ShelfBook>
{
    public void Configure(EntityTypeBuilder<ShelfBook> builder)
    {
        builder.HasKey(b => new { b.ShelfId, b.BookId });
        builder.HasOne(b => b.Book).WithMany().HasForeignKey(b => b.BookId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(b => b.BookId);
    }
}

using CoffeeReader.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeReader.Infrastructure.Persistence.Configurations;

internal sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.Property(b => b.Title).HasMaxLength(Book.MaxTitleLength).IsRequired();
        builder.Property(b => b.SortTitle).HasMaxLength(Book.MaxTitleLength + 8).IsRequired().UseCollation("NOCASE");
        builder.Property(b => b.Publisher).HasMaxLength(256);
        builder.Property(b => b.Language).HasMaxLength(32);
        builder.Property(b => b.PublishedDate).HasMaxLength(64);
        builder.Property(b => b.Isbn).HasMaxLength(32);
        builder.Property(b => b.Identifier).HasMaxLength(256);
        builder.Property(b => b.CoverHref).HasMaxLength(1024);
        builder.Property(b => b.FilePath).HasMaxLength(2048).IsRequired();
        builder.Property(b => b.FileHash).HasMaxLength(64).IsRequired();
        builder.Property(b => b.Format).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(b => b.File);

        builder.HasIndex(b => b.FilePath).IsUnique();
        builder.HasIndex(b => b.FileHash);
        builder.HasIndex(b => b.SortTitle);

        builder.HasOne(b => b.Series).WithMany().HasForeignKey(b => b.SeriesId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(b => b.Authors).WithOne().HasForeignKey(a => a.BookId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(b => b.Authors).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(b => b.Chapters).WithOne().HasForeignKey(c => c.BookId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(b => b.Chapters).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(b => b.TableOfContents).WithOne().HasForeignKey(t => t.BookId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(b => b.TableOfContents).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class BookAuthorConfiguration : IEntityTypeConfiguration<BookAuthor>
{
    public void Configure(EntityTypeBuilder<BookAuthor> builder)
    {
        builder.ToTable("BookAuthors");
        builder.HasKey(a => new { a.BookId, a.AuthorId });
        builder.HasOne(a => a.Author).WithMany().HasForeignKey(a => a.AuthorId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => a.AuthorId);
    }
}

internal sealed class BookChapterConfiguration : IEntityTypeConfiguration<BookChapter>
{
    public void Configure(EntityTypeBuilder<BookChapter> builder)
    {
        builder.ToTable("BookChapters");
        builder.Property(c => c.Href).HasMaxLength(1024).IsRequired();
        builder.Property(c => c.Title).HasMaxLength(512);
        builder.HasIndex(c => new { c.BookId, c.Index }).IsUnique();
    }
}

internal sealed class TocEntryConfiguration : IEntityTypeConfiguration<TocEntry>
{
    public void Configure(EntityTypeBuilder<TocEntry> builder)
    {
        builder.ToTable("TocEntries");
        builder.Property(t => t.Title).HasMaxLength(512).IsRequired();
        builder.Property(t => t.Fragment).HasMaxLength(512);
        builder.HasIndex(t => new { t.BookId, t.Order });
    }
}

internal sealed class AuthorConfiguration : IEntityTypeConfiguration<Author>
{
    public void Configure(EntityTypeBuilder<Author> builder)
    {
        builder.Property(a => a.Name).HasMaxLength(256).IsRequired();
        builder.Property(a => a.NormalizedName).HasMaxLength(512).IsRequired();
        builder.Property(a => a.SortName).HasMaxLength(256).IsRequired().UseCollation("NOCASE");
        builder.HasIndex(a => a.NormalizedName).IsUnique();
    }
}

internal sealed class SeriesConfiguration : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> builder)
    {
        builder.ToTable("Series");
        builder.Property(s => s.Name).HasMaxLength(256).IsRequired();
        builder.Property(s => s.NormalizedName).HasMaxLength(512).IsRequired();
        builder.Property(s => s.SortName).HasMaxLength(264).IsRequired().UseCollation("NOCASE");
        builder.HasIndex(s => s.NormalizedName).IsUnique();
    }
}

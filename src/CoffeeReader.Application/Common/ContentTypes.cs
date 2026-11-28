using CoffeeReader.Domain.Entities;

namespace CoffeeReader.Application.Common;

public static class ContentTypes
{
    public static string ForFormat(BookFormat format) => format switch
    {
        BookFormat.Epub => "application/epub+zip",
        BookFormat.Pdf => "application/pdf",
        BookFormat.Mobi => "application/x-mobipocket-ebook",
        _ => "application/octet-stream",
    };
}

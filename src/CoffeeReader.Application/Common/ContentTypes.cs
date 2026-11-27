using CoffeeReader.Domain.Entities;

namespace CoffeeReader.Application.Common;

public static class ContentTypes
{
    public static string ForFormat(BookFormat format) => format switch
    {
        BookFormat.Epub => "application/epub+zip",
        _ => "application/octet-stream",
    };
}

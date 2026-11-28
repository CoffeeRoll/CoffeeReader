using System.Runtime.InteropServices;
using System.Text;
using CoffeeReader.Application.Abstractions;
using SkiaSharp;
using static CoffeeReader.Infrastructure.Ebooks.Pdf.PdfiumNative;

namespace CoffeeReader.Infrastructure.Ebooks.Pdf;

/// <summary>
/// Serialises all PDFium work (the library isn't thread-safe) and initialises it once.
/// </summary>
internal static class Pdfium
{
    private static readonly Lock Gate = new();
    private static bool _initialized;

    public static T Use<T>(Func<T> work)
    {
        lock (Gate)
        {
            if (!_initialized)
            {
                FPDF_InitLibrary();
                _initialized = true;
            }

            return work();
        }
    }
}

internal sealed record PdfOutlineEntry(string Title, int Depth, int PageIndex);

/// <summary>
/// An open PDF. Use only inside <see cref="Pdfium.Use{T}"/>.
/// </summary>
internal sealed class PdfDocument : IDisposable
{
    private const int MaxOutlineEntries = 5000;
    private const int MaxOutlineDepth = 8;
    private nint _handle;

    private PdfDocument(nint handle) => _handle = handle;

    public int PageCount => FPDF_GetPageCount(_handle);

    public static PdfDocument Open(string path)
    {
        var handle = FPDF_LoadDocument(path, null);
        if (handle == 0)
        {
            var error = (int)FPDF_GetLastError().Value;
            throw error switch
            {
                ErrorPassword => new EbookFormatException("This PDF is password-protected, so it can't be opened."),
                ErrorSecurity => new EbookFormatException("This PDF uses a kind of protection that isn't supported."),
                ErrorFormat => new EbookFormatException("The file isn't a valid PDF."),
                ErrorFile => new IOException("The PDF couldn't be opened."),
                _ => new EbookFormatException($"The PDF couldn't be read (PDFium error {error})."),
            };
        }

        return new PdfDocument(handle);
    }

    public string? MetaText(string tag)
    {
        var length = (int)FPDF_GetMetaText(_handle, tag, null, new CULong(0)).Value;
        if (length <= 2)
        {
            return null;
        }

        var buffer = new byte[length];
        FPDF_GetMetaText(_handle, tag, buffer, new CULong((nuint)length));
        return Clean(Encoding.Unicode.GetString(buffer, 0, length - 2));
    }

    public List<PdfOutlineEntry> Outline()
    {
        var entries = new List<PdfOutlineEntry>();
        var visited = new HashSet<nint>();
        Walk(FPDFBookmark_GetFirstChild(_handle, 0), 0);
        return entries;

        void Walk(nint bookmark, int depth)
        {
            while (bookmark != 0 && entries.Count < MaxOutlineEntries && visited.Add(bookmark))
            {
                var title = BookmarkTitle(bookmark);
                var page = BookmarkPage(bookmark);
                if (title is not null && page >= 0)
                {
                    entries.Add(new PdfOutlineEntry(title.Length > 500 ? title[..500] : title, depth, page));
                }

                if (depth < MaxOutlineDepth)
                {
                    Walk(FPDFBookmark_GetFirstChild(_handle, bookmark), depth + 1);
                }

                bookmark = FPDFBookmark_GetNextSibling(_handle, bookmark);
            }
        }
    }

    public string PageText(int index)
    {
        var page = FPDF_LoadPage(_handle, index);
        if (page == 0)
        {
            return string.Empty;
        }

        try
        {
            var textPage = FPDFText_LoadPage(page);
            if (textPage == 0)
            {
                return string.Empty;
            }

            try
            {
                var count = FPDFText_CountChars(textPage);
                if (count <= 0)
                {
                    return string.Empty;
                }

                var buffer = new ushort[count + 1];
                var written = FPDFText_GetText(textPage, 0, count, buffer);
                var chars = new char[Math.Max(0, Math.Min(count, written - 1))];
                for (var i = 0; i < chars.Length; i++)
                {
                    chars[i] = (char)buffer[i];
                }

                return new string(chars);
            }
            finally
            {
                FPDFText_ClosePage(textPage);
            }
        }
        finally
        {
            FPDF_ClosePage(page);
        }
    }

    /// <summary>Renders a page to a JPEG of the given width (used for cover thumbnails).</summary>
    public byte[]? RenderPage(int index, int width, int maxHeight)
    {
        var page = FPDF_LoadPage(_handle, index);
        if (page == 0)
        {
            return null;
        }

        try
        {
            var pageWidth = FPDF_GetPageWidthF(page);
            var pageHeight = FPDF_GetPageHeightF(page);
            if (pageWidth <= 1 || pageHeight <= 1)
            {
                return null;
            }

            var scale = Math.Min(width / pageWidth, maxHeight / pageHeight);
            var pixelWidth = Math.Max(1, (int)Math.Round(pageWidth * scale));
            var pixelHeight = Math.Max(1, (int)Math.Round(pageHeight * scale));
            var bitmap = FPDFBitmap_Create(pixelWidth, pixelHeight, 1);
            if (bitmap == 0)
            {
                return null;
            }

            try
            {
                FPDFBitmap_FillRect(bitmap, 0, 0, pixelWidth, pixelHeight, new CULong(0xFFFFFFFF));
                FPDF_RenderPageBitmap(bitmap, page, 0, 0, pixelWidth, pixelHeight, 0, RenderAnnotations | RenderReverseByteOrder);
                var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var image = SKImage.FromPixelCopy(info, FPDFBitmap_GetBuffer(bitmap), FPDFBitmap_GetStride(bitmap));
                using var encoded = image?.Encode(SKEncodedImageFormat.Jpeg, 90);
                return encoded?.ToArray();
            }
            finally
            {
                FPDFBitmap_Destroy(bitmap);
            }
        }
        finally
        {
            FPDF_ClosePage(page);
        }
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            FPDF_CloseDocument(_handle);
            _handle = 0;
        }
    }

    private static string? BookmarkTitle(nint bookmark)
    {
        var length = (int)FPDFBookmark_GetTitle(bookmark, null, new CULong(0)).Value;
        if (length <= 2)
        {
            return null;
        }

        var buffer = new byte[length];
        FPDFBookmark_GetTitle(bookmark, buffer, new CULong((nuint)length));
        return Clean(Encoding.Unicode.GetString(buffer, 0, length - 2));
    }

    private int BookmarkPage(nint bookmark)
    {
        var dest = FPDFBookmark_GetDest(_handle, bookmark);
        if (dest == 0)
        {
            var action = FPDFBookmark_GetAction(bookmark);
            dest = action == 0 ? 0 : FPDFAction_GetDest(_handle, action);
        }

        return dest == 0 ? -1 : FPDFDest_GetDestPageIndex(_handle, dest);
    }

    private static string? Clean(string value)
    {
        var cleaned = Domain.Common.TextNormalization.CollapseWhitespace(value.Replace('\0', ' ')).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}

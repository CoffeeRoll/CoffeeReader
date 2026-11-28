using System.Runtime.InteropServices;

namespace CoffeeReader.Infrastructure.Ebooks.Pdf;

/// <summary>
/// The parts of the PDFium C API CoffeeReader uses (see fpdfview.h, fpdf_doc.h, fpdf_text.h).
/// PDFium isn't thread-safe: only call these through <see cref="Pdfium.Use{T}"/>.
/// </summary>
internal static partial class PdfiumNative
{
    private const string Library = "pdfium";

    public const int ErrorFile = 2;
    public const int ErrorFormat = 3;
    public const int ErrorPassword = 4;
    public const int ErrorSecurity = 5;

    public const int RenderAnnotations = 0x01;
    public const int RenderReverseByteOrder = 0x10;

    [LibraryImport(Library)]
    public static partial void FPDF_InitLibrary();

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint FPDF_LoadDocument(string filePath, string? password);

    [LibraryImport(Library)]
    public static partial CULong FPDF_GetLastError();

    [LibraryImport(Library)]
    public static partial void FPDF_CloseDocument(nint document);

    [LibraryImport(Library)]
    public static partial int FPDF_GetPageCount(nint document);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial CULong FPDF_GetMetaText(nint document, string tag, byte[]? buffer, CULong length);

    [LibraryImport(Library)]
    public static partial nint FPDF_LoadPage(nint document, int pageIndex);

    [LibraryImport(Library)]
    public static partial void FPDF_ClosePage(nint page);

    [LibraryImport(Library)]
    public static partial float FPDF_GetPageWidthF(nint page);

    [LibraryImport(Library)]
    public static partial float FPDF_GetPageHeightF(nint page);

    [LibraryImport(Library)]
    public static partial nint FPDFBitmap_Create(int width, int height, int alpha);

    [LibraryImport(Library)]
    public static partial int FPDFBitmap_FillRect(nint bitmap, int left, int top, int width, int height, CULong color);

    [LibraryImport(Library)]
    public static partial void FPDF_RenderPageBitmap(nint bitmap, nint page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    [LibraryImport(Library)]
    public static partial nint FPDFBitmap_GetBuffer(nint bitmap);

    [LibraryImport(Library)]
    public static partial int FPDFBitmap_GetStride(nint bitmap);

    [LibraryImport(Library)]
    public static partial void FPDFBitmap_Destroy(nint bitmap);

    [LibraryImport(Library)]
    public static partial nint FPDFBookmark_GetFirstChild(nint document, nint bookmark);

    [LibraryImport(Library)]
    public static partial nint FPDFBookmark_GetNextSibling(nint document, nint bookmark);

    [LibraryImport(Library)]
    public static partial CULong FPDFBookmark_GetTitle(nint bookmark, byte[]? buffer, CULong length);

    [LibraryImport(Library)]
    public static partial nint FPDFBookmark_GetDest(nint document, nint bookmark);

    [LibraryImport(Library)]
    public static partial nint FPDFBookmark_GetAction(nint bookmark);

    [LibraryImport(Library)]
    public static partial nint FPDFAction_GetDest(nint document, nint action);

    [LibraryImport(Library)]
    public static partial int FPDFDest_GetDestPageIndex(nint document, nint dest);

    [LibraryImport(Library)]
    public static partial nint FPDFText_LoadPage(nint page);

    [LibraryImport(Library)]
    public static partial void FPDFText_ClosePage(nint textPage);

    [LibraryImport(Library)]
    public static partial int FPDFText_CountChars(nint textPage);

    [LibraryImport(Library)]
    public static partial int FPDFText_GetText(nint textPage, int startIndex, int count, ushort[] result);
}

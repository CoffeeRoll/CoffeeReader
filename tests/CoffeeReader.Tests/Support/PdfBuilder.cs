using System.Globalization;
using System.Text;

namespace CoffeeReader.Tests.Support;

/// <summary>
/// Writes small, valid PDFs for tests: text pages in Helvetica, document info and a nested outline.
/// </summary>
public sealed class PdfBuilder
{
    private readonly List<(string Heading, string[] Lines)> _pages = [];
    private readonly List<(string Title, int Page, int Depth)> _outline = [];
    private string? _title = "Test PDF";
    private string? _author = "Test Author";
    private string? _subject;

    public PdfBuilder Title(string? title)
    {
        _title = title;
        return this;
    }

    public PdfBuilder Author(string? author)
    {
        _author = author;
        return this;
    }

    public PdfBuilder Subject(string subject)
    {
        _subject = subject;
        return this;
    }

    public PdfBuilder Page(string heading, params string[] lines)
    {
        _pages.Add((heading, lines));
        return this;
    }

    /// <summary>Adds an outline entry pointing at a zero-based page. Depth 1 nests under the previous depth-0 entry.</summary>
    public PdfBuilder Bookmark(string title, int page, int depth = 0)
    {
        _outline.Add((title, page, depth));
        return this;
    }

    public string WriteTo(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, Build());
        return path;
    }

    public byte[] Build()
    {
        if (_pages.Count == 0)
        {
            Page("Page One", "It was a bright cold day in April.");
        }

        // Object numbers: 1 catalog, 2 pages, 3 font, 4 info, 5 outlines, then pages/contents, then outline items.
        var objects = new SortedDictionary<int, string>();
        var pageObjects = new List<int>();
        var next = 6;
        for (var i = 0; i < _pages.Count; i++)
        {
            var page = next++;
            var contents = next++;
            pageObjects.Add(page);
            var stream = PageStream(i, _pages[i].Heading, _pages[i].Lines);
            objects[page] = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {contents} 0 R >>";
            objects[contents] = $"<< /Length {Encoding.Latin1.GetByteCount(stream)} >>\nstream\n{stream}\nendstream";
        }

        objects[1] = "<< /Type /Catalog /Pages 2 0 R /Outlines 5 0 R /PageMode /UseOutlines >>";
        objects[2] = $"<< /Type /Pages /Kids [{string.Join(' ', pageObjects.Select(p => $"{p} 0 R"))}] /Count {pageObjects.Count} >>";
        objects[3] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>";
        var info = new StringBuilder("<< ");
        if (_title is not null) info.Append($"/Title {Text(_title)} ");
        if (_author is not null) info.Append($"/Author {Text(_author)} ");
        if (_subject is not null) info.Append($"/Subject {Text(_subject)} ");
        info.Append("/CreationDate (D:20210315093000Z) /Producer (CoffeeReader tests) >>");
        objects[4] = info.ToString();

        // Outline tree (two levels).
        var top = new List<int>();
        var children = new Dictionary<int, List<int>>();
        var items = new Dictionary<int, (string Title, int Page)>();
        foreach (var (title, page, depth) in _outline)
        {
            var id = next++;
            items[id] = (title, page);
            if (depth == 0 || top.Count == 0)
            {
                top.Add(id);
                children[id] = [];
            }
            else
            {
                children[top[^1]].Add(id);
            }
        }

        void Link(List<int> siblings, int parent)
        {
            for (var i = 0; i < siblings.Count; i++)
            {
                var id = siblings[i];
                var kids = children.GetValueOrDefault(id) ?? [];
                var entry = new StringBuilder($"<< /Title {Text(items[id].Title)} /Parent {parent} 0 R /Dest [{pageObjects[items[id].Page]} 0 R /Fit]");
                if (i > 0) entry.Append($" /Prev {siblings[i - 1]} 0 R");
                if (i < siblings.Count - 1) entry.Append($" /Next {siblings[i + 1]} 0 R");
                if (kids.Count > 0) entry.Append($" /First {kids[0]} 0 R /Last {kids[^1]} 0 R /Count {kids.Count}");
                objects[id] = entry.Append(" >>").ToString();
                Link(kids, id);
            }
        }

        Link(top, 5);
        objects[5] = top.Count == 0
            ? "<< /Type /Outlines /Count 0 >>"
            : $"<< /Type /Outlines /First {top[0]} 0 R /Last {top[^1]} 0 R /Count {items.Count} >>";

        var output = new MemoryStream();
        void Write(string value) => output.Write(Encoding.Latin1.GetBytes(value));
        Write("%PDF-1.7\n%âãÏÓ\n");
        var offsets = new Dictionary<int, long>();
        foreach (var (id, body) in objects)
        {
            offsets[id] = output.Position;
            Write($"{id} 0 obj\n{body}\nendobj\n");
        }

        var xref = output.Position;
        var size = objects.Keys.Max() + 1;
        Write($"xref\n0 {size}\n0000000000 65535 f \n");
        for (var id = 1; id < size; id++)
        {
            Write(offsets.TryGetValue(id, out var offset) ? $"{offset:D10} 00000 n \n" : "0000000000 65535 f \n");
        }

        Write($"trailer\n<< /Size {size} /Root 1 0 R /Info 4 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private static string PageStream(int index, string heading, string[] lines)
    {
        var builder = new StringBuilder();
        if (index == 0)
        {
            // A coloured first page so the cover thumbnail is easy to recognise.
            builder.Append("0.47 0.27 0.13 rg 0 0 612 792 re f 1 1 1 rg\n");
        }

        builder.Append(CultureInfo.InvariantCulture, $"BT /F1 28 Tf 72 690 Td {Text(heading)} Tj ET\n");
        builder.Append("BT /F1 12 Tf 72 650 Td 16 TL\n");
        foreach (var line in lines)
        {
            builder.Append(Text(line)).Append(" Tj T*\n");
        }

        builder.Append("ET");
        return builder.ToString();
    }

    private static string Text(string value) =>
        "(" + value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + ")";
}

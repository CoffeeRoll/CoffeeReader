using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace CoffeeReader.Tests.Support;

/// <summary>
/// Writes MOBI 6 books (PalmDB + PalmDOC compression + EXTH) for tests, shaped like KindleGen/Calibre output:
/// a guide-referenced contents page, filepos links, page breaks between chapters and image records.
/// </summary>
public sealed partial class MobiBuilder
{
    private readonly List<(string Title, string Body)> _chapters = [];
    private readonly List<string> _authors = [];
    private string _title = "Test Book";
    private string? _description;
    private bool _compress = true;
    private bool _withCover = true;
    private bool _withToc = true;
    private bool _utf8 = true;
    private int _encryption;
    private int _version = 6;

    public MobiBuilder Title(string title) => Set(() => _title = title);

    public MobiBuilder Author(string author) => Set(() => _authors.Add(author));

    public MobiBuilder Description(string description) => Set(() => _description = description);

    public MobiBuilder Uncompressed() => Set(() => _compress = false);

    public MobiBuilder WithoutCover() => Set(() => _withCover = false);

    public MobiBuilder WithoutToc() => Set(() => _withToc = false);

    public MobiBuilder Windows1252() => Set(() => _utf8 = false);

    public MobiBuilder Encrypted() => Set(() => _encryption = 2);

    public MobiBuilder Kf8() => Set(() => _version = 8);

    /// <summary>
    /// Adds a chapter. In the body, <c>{{link:N|text}}</c> links to chapter N's start and <c>{{image}}</c> embeds the cover.
    /// </summary>
    public MobiBuilder Chapter(string title, string body) => Set(() => _chapters.Add((title, body)));

    public string WriteTo(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, Build());
        return path;
    }

    public byte[] Build()
    {
        if (_chapters.Count == 0)
        {
            Chapter("Chapter One", "<p>It was a bright cold day in April.</p>");
        }

        var encoding = _utf8 ? Encoding.UTF8 : CodePage1252();
        var text = encoding.GetBytes(BuildMarkup(encoding));
        var image = EpubBuilder.CreatePng(200, 300);

        var textRecords = new List<byte[]>();
        for (var offset = 0; offset < text.Length; offset += 4096)
        {
            var chunk = text.AsSpan(offset, Math.Min(4096, text.Length - offset)).ToArray();
            var body = _compress ? CompressPalmDoc(chunk) : chunk;

            // Trailing entry for "multibyte overlap" (extra flags bit 0) with zero extra bytes.
            textRecords.Add([.. body, 0x00]);
        }

        var firstImage = 1 + textRecords.Count;
        var records = new List<byte[]> { BuildRecord0(text.Length, textRecords.Count, firstImage, encoding) };
        records.AddRange(textRecords);
        if (_withCover)
        {
            records.Add(image);
        }

        records.Add([0xE9, 0x8E, 0x0D, 0x0A]); // EOF record
        return BuildPalmDatabase(records);
    }

    private string BuildMarkup(Encoding encoding)
    {
        // Chapter starts are marked, links use fixed-width placeholders, then real byte offsets are filled in.
        var builder = new StringBuilder("<html><head><guide>");
        if (_withToc)
        {
            builder.Append("<reference type=\"toc\" title=\"Table of Contents\" filepos=@@POS:toc@@ />");
        }

        builder.Append("</guide></head><body>");
        if (_withToc)
        {
            builder.Append("@@START:toc@@<h1>Contents</h1>");
            for (var i = 0; i < _chapters.Count; i++)
            {
                builder.Append($"<p><a filepos=@@POS:{i}@@>{_chapters[i].Title}</a></p>");
            }

            builder.Append("<mbp:pagebreak/>");
        }

        for (var i = 0; i < _chapters.Count; i++)
        {
            var body = LinkToken().Replace(_chapters[i].Body, m => $"<a filepos=@@POS:{m.Groups[1].Value}@@>{m.Groups[2].Value}</a>");
            body = body.Replace("{{image}}", "<img recindex=\"00001\" align=\"baseline\" />");
            builder.Append($"@@START:{i}@@<h2>{_chapters[i].Title}</h2>{body}<mbp:pagebreak/>");
        }

        builder.Append("</body></html>");
        var markup = PositionToken().Replace(builder.ToString(), "0000000000");

        var starts = new Dictionary<string, int>();
        while (StartToken().Match(markup) is { Success: true } start)
        {
            starts[start.Groups[1].Value] = encoding.GetByteCount(markup[..start.Index]);
            markup = markup.Remove(start.Index, start.Length);
        }

        var withPlaceholders = PositionToken().Replace(builder.ToString(), m => $"@@P:{m.Groups[1].Value}@@");
        withPlaceholders = StartToken().Replace(withPlaceholders, string.Empty);
        return ResolvedToken().Replace(withPlaceholders, m => starts[m.Groups[1].Value].ToString("D10"));
    }

    private byte[] BuildRecord0(int textLength, int textRecordCount, int firstImage, Encoding encoding)
    {
        var exth = new List<(int Type, byte[] Data)>();
        foreach (var author in _authors)
        {
            exth.Add((100, encoding.GetBytes(author)));
        }

        exth.Add((101, encoding.GetBytes("Test Press")));
        if (_description is not null)
        {
            exth.Add((103, encoding.GetBytes(_description)));
        }

        exth.Add((104, encoding.GetBytes("978-0-00-000000-2")));
        exth.Add((106, encoding.GetBytes("2019-04-02T00:00:00+00:00")));
        exth.Add((503, encoding.GetBytes(_title)));
        exth.Add((524, encoding.GetBytes("en")));
        if (_withCover)
        {
            exth.Add((201, BigEndian(0)));
        }

        var exthBytes = new List<byte>();
        exthBytes.AddRange("EXTH"u8.ToArray());
        var exthLength = 12 + exth.Sum(e => 8 + e.Data.Length);
        var padding = (4 - (exthLength % 4)) % 4;
        exthBytes.AddRange(BigEndian(exthLength + padding));
        exthBytes.AddRange(BigEndian(exth.Count));
        foreach (var (type, data) in exth)
        {
            exthBytes.AddRange(BigEndian(type));
            exthBytes.AddRange(BigEndian(8 + data.Length));
            exthBytes.AddRange(data);
        }

        exthBytes.AddRange(new byte[padding]);

        const int headerLength = 232;
        var record = new byte[16 + headerLength];
        void U16(int offset, int value) => BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(offset), (ushort)value);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(offset), value);

        U16(0, _compress ? 2 : 1);
        U32(4, (uint)textLength);
        U16(8, textRecordCount);
        U16(10, 4096);
        U16(12, _encryption);
        "MOBI"u8.CopyTo(record.AsSpan(16));
        U32(20, headerLength);
        U32(24, 2);
        U32(28, (uint)(_utf8 ? 65001 : 1252));
        U32(32, 0x12345678);
        U32(36, (uint)_version);
        for (var offset = 40; offset < 80; offset += 4)
        {
            U32(offset, 0xFFFFFFFF);
        }

        var nameOffset = record.Length + exthBytes.Count;
        var name = encoding.GetBytes(_title);
        U32(80, (uint)firstImage);
        U32(84, (uint)nameOffset);
        U32(88, (uint)name.Length);
        U32(92, 9);
        U32(104, 6);
        U32(108, (uint)firstImage);
        U32(128, 0x50);
        U32(164, 0xFFFFFFFF);
        U32(168, 0xFFFFFFFF);
        U16(192, 1);
        U16(194, textRecordCount);
        U32(196, 1);
        U32(200, 0xFFFFFFFF);
        U32(208, 0xFFFFFFFF);
        U32(224, 0xFFFFFFFF);
        U32(232, 0xFFFFFFFF);
        U32(236, 0xFFFFFFFF);
        U32(240, 1); // extra record data flags: multibyte overlap
        U32(244, 0xFFFFFFFF);

        return [.. record, .. exthBytes, .. name, 0, 0, 0, 0];
    }

    private byte[] BuildPalmDatabase(List<byte[]> records)
    {
        var header = new byte[78];
        var name = Encoding.ASCII.GetBytes(new string(_title.Where(c => c < 128 && c != ' ').Take(31).ToArray()));
        name.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(36), 0x5F3E2A10);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(40), 0x5F3E2A10);
        "BOOKMOBI"u8.CopyTo(header.AsSpan(60));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(68), (uint)((records.Count * 2) - 1));
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(76), (ushort)records.Count);

        var output = new List<byte>(header);
        var offset = 78 + (records.Count * 8) + 2;
        for (var i = 0; i < records.Count; i++)
        {
            output.AddRange(BigEndian(offset));
            output.AddRange(BigEndian(i * 2));
            offset += records[i].Length;
        }

        output.Add(0);
        output.Add(0);
        foreach (var record in records)
        {
            output.AddRange(record);
        }

        return output.ToArray();
    }

    /// <summary>A small, correct PalmDOC compressor (greedy LZ77, space pairs and literal runs).</summary>
    public static byte[] CompressPalmDoc(byte[] data)
    {
        var output = new List<byte>(data.Length);
        var i = 0;
        while (i < data.Length)
        {
            int bestLength = 0, bestDistance = 0;
            for (var j = i - 1; j >= Math.Max(0, i - 2047) && i + 3 <= data.Length; j--)
            {
                var length = 0;
                while (length < 10 && i + length < data.Length && data[j + length] == data[i + length])
                {
                    length++;
                }

                if (length > bestLength)
                {
                    bestLength = length;
                    bestDistance = i - j;
                    if (length == 10)
                    {
                        break;
                    }
                }
            }

            if (bestLength >= 3)
            {
                var pair = 0x8000 | (bestDistance << 3) | (bestLength - 3);
                output.Add((byte)(pair >> 8));
                output.Add((byte)pair);
                i += bestLength;
                continue;
            }

            var c = data[i];
            if (c == ' ' && i + 1 < data.Length && data[i + 1] is >= 0x40 and <= 0x7F)
            {
                output.Add((byte)(data[i + 1] ^ 0x80));
                i += 2;
            }
            else if (c is >= 1 and <= 8 or >= 0x80)
            {
                var run = 1;
                while (run < 8 && i + run < data.Length && data[i + run] is >= 1 and <= 8 or >= 0x80)
                {
                    run++;
                }

                output.Add((byte)run);
                output.AddRange(data.AsSpan(i, run).ToArray());
                i += run;
            }
            else
            {
                output.Add(c);
                i++;
            }
        }

        return output.ToArray();
    }

    private static byte[] BigEndian(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    private static Encoding CodePage1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    private MobiBuilder Set(Action action)
    {
        action();
        return this;
    }

    [GeneratedRegex(@"\{\{link:(\d+)\|([^}]*)\}\}")]
    private static partial Regex LinkToken();

    [GeneratedRegex(@"@@POS:([^@]+)@@")]
    private static partial Regex PositionToken();

    [GeneratedRegex(@"@@START:([^@]+)@@")]
    private static partial Regex StartToken();

    [GeneratedRegex(@"@@P:([^@]+)@@")]
    private static partial Regex ResolvedToken();
}

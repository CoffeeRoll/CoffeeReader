using System.Buffers.Binary;
using System.Text;
using CoffeeReader.Application.Abstractions;

namespace CoffeeReader.Infrastructure.Ebooks.Mobi;

/// <summary>
/// Record 0 of a MOBI book: the PalmDOC header, the MOBI header and the EXTH metadata block.
/// </summary>
internal sealed class MobiHeader
{
    public const int NoCompression = 1;
    public const int PalmDocCompression = 2;
    public const int HuffCdicCompression = 17480;

    static MobiHeader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private MobiHeader()
    {
    }

    public int Compression { get; private init; }

    public int TextLength { get; private init; }

    public int TextRecordCount { get; private init; }

    public int Encryption { get; private init; }

    public int Version { get; private init; }

    public Encoding TextEncoding { get; private init; } = Encoding.UTF8;

    public string? FullName { get; private init; }

    public int FirstImageIndex { get; private init; }

    public int HuffRecordIndex { get; private init; }

    public int HuffRecordCount { get; private init; }

    public int ExtraFlags { get; private init; }

    public IReadOnlyDictionary<int, IReadOnlyList<byte[]>> Exth { get; private init; } = new Dictionary<int, IReadOnlyList<byte[]>>();

    public static MobiHeader Parse(byte[] record)
    {
        if (record.Length < 24 || record[16] != 'M' || record[17] != 'O' || record[18] != 'B' || record[19] != 'I')
        {
            throw new EbookFormatException("The file isn't a MOBI book (the MOBI header is missing).");
        }

        uint U32(int offset) => offset + 4 <= record.Length ? BinaryPrimitives.ReadUInt32BigEndian(record.AsSpan(offset)) : 0;
        int U16(int offset) => offset + 2 <= record.Length ? BinaryPrimitives.ReadUInt16BigEndian(record.AsSpan(offset)) : 0;

        var headerLength = (int)U32(20);
        var codepage = (int)U32(28);
        var encoding = codepage == 65001 ? Encoding.UTF8 : Encoding.GetEncoding(codepage is > 0 and < 65536 ? codepage : 1252);

        string? fullName = null;
        var nameOffset = (int)U32(84);
        var nameLength = (int)U32(88);
        if (nameOffset > 0 && nameLength > 0 && nameOffset + nameLength <= record.Length)
        {
            fullName = encoding.GetString(record, nameOffset, nameLength).Trim('\0', ' ');
        }

        var exth = new Dictionary<int, IReadOnlyList<byte[]>>();
        var hasExth = (U32(128) & 0x40) != 0;
        var exthStart = 16 + headerLength;
        if (hasExth && exthStart + 12 <= record.Length && record[exthStart] == 'E' && record[exthStart + 1] == 'X')
        {
            var count = (int)U32(exthStart + 8);
            var position = exthStart + 12;
            var lists = new Dictionary<int, List<byte[]>>();
            for (var i = 0; i < count && position + 8 <= record.Length; i++)
            {
                var type = (int)U32(position);
                var length = (int)U32(position + 4);
                if (length < 8 || position + length > record.Length)
                {
                    break;
                }

                if (!lists.TryGetValue(type, out var list))
                {
                    lists[type] = list = [];
                }

                list.Add(record.AsSpan(position + 8, length - 8).ToArray());
                position += length;
            }

            foreach (var (type, list) in lists)
            {
                exth[type] = list;
            }
        }

        return new MobiHeader
        {
            Compression = U16(0),
            TextLength = (int)U32(4),
            TextRecordCount = U16(8),
            Encryption = U16(12),
            Version = (int)U32(36),
            TextEncoding = encoding,
            FullName = fullName,
            FirstImageIndex = (int)U32(108),
            HuffRecordIndex = (int)U32(112),
            HuffRecordCount = (int)U32(116),
            ExtraFlags = headerLength >= 0xE4 ? U16(0xF2) : 0,
            Exth = exth,
        };
    }

    public string? ExthString(int type) =>
        Exth.TryGetValue(type, out var values) && values.Count > 0 ? Clean(TextEncoding.GetString(values[0])) : null;

    public IEnumerable<string> ExthStrings(int type) =>
        Exth.TryGetValue(type, out var values) ? values.Select(v => Clean(TextEncoding.GetString(v))).OfType<string>() : [];

    public int? ExthNumber(int type) =>
        Exth.TryGetValue(type, out var values) && values.Count > 0 && values[0].Length == 4
            ? (int)BinaryPrimitives.ReadUInt32BigEndian(values[0])
            : null;

    private static string? Clean(string value)
    {
        var trimmed = value.Trim('\0', ' ', '\r', '\n', '\t');
        return trimmed.Length == 0 ? null : trimmed;
    }
}

/// <summary>EXTH record types used by CoffeeReader.</summary>
internal static class Exth
{
    public const int Author = 100;
    public const int Publisher = 101;
    public const int Description = 103;
    public const int Isbn = 104;
    public const int PublishedDate = 106;
    public const int CoverOffset = 201;
    public const int ThumbnailOffset = 202;
    public const int UpdatedTitle = 503;
    public const int Language = 524;
}

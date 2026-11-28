using System.Buffers.Binary;
using CoffeeReader.Application.Abstractions;

namespace CoffeeReader.Infrastructure.Ebooks.Mobi;

internal static class MobiDecompression
{
    /// <summary>
    /// Removes the trailing entries some MOBI writers append to text records (flagged in the header).
    /// </summary>
    public static ReadOnlySpan<byte> TrimTrailingEntries(ReadOnlySpan<byte> record, int extraFlags)
    {
        var size = record.Length;
        var trailing = 0;
        for (var flags = extraFlags >> 1; flags != 0; flags >>= 1)
        {
            if ((flags & 1) != 0)
            {
                trailing += TrailingEntrySize(record, size - trailing);
            }
        }

        // Bit 0: multibyte character overlap bytes.
        if ((extraFlags & 1) != 0 && size - trailing - 1 >= 0)
        {
            trailing += (record[size - trailing - 1] & 0x3) + 1;
        }

        return record[..Math.Max(0, size - trailing)];
    }

    private static int TrailingEntrySize(ReadOnlySpan<byte> record, int size)
    {
        int result = 0, shift = 0;
        while (size > 0)
        {
            var value = record[size - 1];
            result |= (value & 0x7F) << shift;
            shift += 7;
            size--;
            if ((value & 0x80) != 0 || shift >= 28)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>PalmDOC compression: LZ77 with a 2 KB window plus space-and-character pairs.</summary>
    public static void PalmDoc(ReadOnlySpan<byte> input, List<byte> output)
    {
        var i = 0;
        while (i < input.Length)
        {
            var c = input[i++];
            if (c is >= 1 and <= 8)
            {
                // The next c bytes are literal.
                for (var n = 0; n < c && i < input.Length; n++)
                {
                    output.Add(input[i++]);
                }
            }
            else if (c < 0x80)
            {
                output.Add(c);
            }
            else if (c >= 0xC0)
            {
                output.Add((byte)' ');
                output.Add((byte)(c ^ 0x80));
            }
            else if (i < input.Length)
            {
                var pair = ((c << 8) | input[i++]) & 0x3FFF;
                var distance = pair >> 3;
                var length = (pair & 7) + 3;
                var from = output.Count - distance;
                if (distance == 0 || from < 0)
                {
                    throw new EbookFormatException("The MOBI text is corrupt (bad back-reference).");
                }

                for (var n = 0; n < length; n++)
                {
                    output.Add(output[from + n]);
                }
            }
        }
    }
}

/// <summary>
/// The Huffman/CDIC compression KindleGen uses for larger books.
/// </summary>
internal sealed class HuffCdicDecoder
{
    private readonly (int CodeLength, bool Terminal, ulong MaxCode)[] _table1 = new (int, bool, ulong)[256];
    private readonly ulong[] _minCode = new ulong[33];
    private readonly ulong[] _maxCode = new ulong[33];
    private readonly List<(byte[]? Data, bool Expanded)> _dictionary = [];
    private int _depth;

    public HuffCdicDecoder(byte[] huff, IEnumerable<byte[]> cdics)
    {
        if (huff.Length < 24 || huff[0] != 'H' || huff[1] != 'U' || huff[2] != 'F' || huff[3] != 'F')
        {
            throw new EbookFormatException("The MOBI file's Huffman table is missing.");
        }

        var table1 = (int)BinaryPrimitives.ReadUInt32BigEndian(huff.AsSpan(8));
        var table2 = (int)BinaryPrimitives.ReadUInt32BigEndian(huff.AsSpan(12));
        for (var i = 0; i < 256; i++)
        {
            var value = BinaryPrimitives.ReadUInt32BigEndian(huff.AsSpan(table1 + (i * 4)));
            var codeLength = (int)(value & 0x1F);
            var terminal = (value & 0x80) != 0;
            var maxCode = (((ulong)(value >> 8) + 1) << (32 - codeLength)) - 1;
            _table1[i] = (codeLength, terminal, maxCode);
        }

        _minCode[0] = 0;
        _maxCode[0] = (1UL << 32) - 1;
        for (var length = 1; length <= 32; length++)
        {
            var min = BinaryPrimitives.ReadUInt32BigEndian(huff.AsSpan(table2 + ((length - 1) * 8)));
            var max = BinaryPrimitives.ReadUInt32BigEndian(huff.AsSpan(table2 + ((length - 1) * 8) + 4));
            _minCode[length] = (ulong)min << (32 - length);
            _maxCode[length] = (((ulong)max + 1) << (32 - length)) - 1;
        }

        foreach (var cdic in cdics)
        {
            LoadCdic(cdic);
        }
    }

    public void Decode(ReadOnlySpan<byte> input, List<byte> output)
    {
        if (++_depth > 32)
        {
            throw new EbookFormatException("The MOBI text is corrupt (Huffman dictionary loop).");
        }

        try
        {
            var padded = new byte[input.Length + 8];
            input.CopyTo(padded);
            long bitsLeft = input.Length * 8L;
            var position = 0;
            var x = BinaryPrimitives.ReadUInt64BigEndian(padded);
            var n = 32;
            while (true)
            {
                if (n <= 0)
                {
                    position += 4;
                    x = BinaryPrimitives.ReadUInt64BigEndian(padded.AsSpan(position));
                    n += 32;
                }

                var code = (x >> n) & 0xFFFFFFFF;
                var (codeLength, terminal, maxCode) = _table1[(int)(code >> 24)];
                if (!terminal)
                {
                    while (codeLength < 32 && code < _minCode[codeLength])
                    {
                        codeLength++;
                    }

                    maxCode = _maxCode[codeLength];
                }

                n -= codeLength;
                bitsLeft -= codeLength;
                if (bitsLeft < 0 || codeLength == 0)
                {
                    break;
                }

                var index = (int)((maxCode - code) >> (32 - codeLength));
                if (index < 0 || index >= _dictionary.Count)
                {
                    throw new EbookFormatException("The MOBI text is corrupt (bad Huffman code).");
                }

                var (data, expanded) = _dictionary[index];
                if (data is null)
                {
                    throw new EbookFormatException("The MOBI text is corrupt (Huffman dictionary loop).");
                }

                if (!expanded)
                {
                    _dictionary[index] = (null, false);
                    var buffer = new List<byte>(data.Length * 2);
                    Decode(data, buffer);
                    data = buffer.ToArray();
                    _dictionary[index] = (data, true);
                }

                output.AddRange(data);
            }
        }
        finally
        {
            _depth--;
        }
    }

    private void LoadCdic(byte[] cdic)
    {
        if (cdic.Length < 16 || cdic[0] != 'C' || cdic[1] != 'D' || cdic[2] != 'I' || cdic[3] != 'C')
        {
            throw new EbookFormatException("The MOBI file's Huffman dictionary is corrupt.");
        }

        var phrases = (int)BinaryPrimitives.ReadUInt32BigEndian(cdic.AsSpan(8));
        var bits = (int)BinaryPrimitives.ReadUInt32BigEndian(cdic.AsSpan(12));
        var count = Math.Min(1 << bits, phrases - _dictionary.Count);
        for (var i = 0; i < count; i++)
        {
            var offset = BinaryPrimitives.ReadUInt16BigEndian(cdic.AsSpan(16 + (i * 2)));
            var length = BinaryPrimitives.ReadUInt16BigEndian(cdic.AsSpan(16 + offset));
            var data = cdic.AsSpan(18 + offset, length & 0x7FFF).ToArray();
            _dictionary.Add((data, (length & 0x8000) != 0));
        }
    }
}

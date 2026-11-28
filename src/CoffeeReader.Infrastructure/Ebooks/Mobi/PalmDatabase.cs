using System.Buffers.Binary;
using CoffeeReader.Application.Abstractions;

namespace CoffeeReader.Infrastructure.Ebooks.Mobi;

/// <summary>
/// A Palm database (PDB): the container MOBI books are stored in. Records are read on demand.
/// </summary>
internal sealed class PalmDatabase : IDisposable
{
    private const int HeaderSize = 78;
    private readonly FileStream _stream;
    private readonly long[] _offsets;

    private PalmDatabase(FileStream stream, long[] offsets, string type, string creator)
    {
        _stream = stream;
        _offsets = offsets;
        Type = type;
        Creator = creator;
    }

    public string Type { get; }

    public string Creator { get; }

    public int RecordCount => _offsets.Length;

    public static PalmDatabase Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192);
        try
        {
            Span<byte> header = stackalloc byte[HeaderSize];
            if (stream.Read(header) != HeaderSize)
            {
                throw new EbookFormatException("The file is too short to be a MOBI book.");
            }

            var type = System.Text.Encoding.ASCII.GetString(header.Slice(60, 4));
            var creator = System.Text.Encoding.ASCII.GetString(header.Slice(64, 4));
            var count = BinaryPrimitives.ReadUInt16BigEndian(header[76..]);
            if (count == 0)
            {
                throw new EbookFormatException("The MOBI file has no records.");
            }

            var list = new byte[count * 8];
            if (stream.Read(list) != list.Length)
            {
                throw new EbookFormatException("The MOBI file's record list is truncated.");
            }

            var offsets = new long[count];
            for (var i = 0; i < count; i++)
            {
                offsets[i] = BinaryPrimitives.ReadUInt32BigEndian(list.AsSpan(i * 8));
                if (offsets[i] > stream.Length || (i > 0 && offsets[i] < offsets[i - 1]))
                {
                    throw new EbookFormatException("The MOBI file's record list is corrupt.");
                }
            }

            return new PalmDatabase(stream, offsets, type, creator);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public byte[] ReadRecord(int index)
    {
        if (index < 0 || index >= _offsets.Length)
        {
            throw new EbookFormatException($"The MOBI file refers to a record ({index}) that doesn't exist.");
        }

        var start = _offsets[index];
        var end = index + 1 < _offsets.Length ? _offsets[index + 1] : _stream.Length;
        var buffer = new byte[end - start];
        _stream.Position = start;
        _stream.ReadExactly(buffer);
        return buffer;
    }

    public void Dispose() => _stream.Dispose();
}

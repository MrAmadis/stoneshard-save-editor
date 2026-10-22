using System.Buffers.Binary;
using System.Text;

namespace StoneshardSaveEditor.GameData;

/// <summary>
/// GameMaker's <c>data.win</c>: a <c>FORM</c> container of named chunks (SPRT, TPAG, TXTR, OBJT, …).
/// Pointers inside chunks are absolute file offsets.
/// </summary>
public sealed class DataWinFile
{
    private readonly byte[] _bytes;
    private readonly Dictionary<string, (int Offset, int Length)> _chunks = new(StringComparer.Ordinal);

    private DataWinFile(string path, byte[] bytes)
    {
        Path = path;
        _bytes = bytes;
        if (bytes.Length < 8 || !bytes.AsSpan(0, 4).SequenceEqual("FORM"u8))
            throw new InvalidDataException($"{path} is not a GameMaker data file");
        var pos = 8;
        while (pos + 8 <= bytes.Length)
        {
            var name = Encoding.ASCII.GetString(bytes, pos, 4);
            var size = (int)U32(pos + 4);
            _chunks[name] = (pos + 8, size);
            pos += 8 + size;
        }
    }

    public string Path { get; }

    public static DataWinFile Load(string path) => new(path, File.ReadAllBytes(path));

    public (int Offset, int Length) Chunk(string name) =>
        _chunks.TryGetValue(name, out var chunk) ? chunk : throw new InvalidDataException($"data.win has no {name} chunk");

    public uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(offset));

    public int I32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(offset));

    public ushort U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(offset));

    /// <summary>Reads a GameMaker string: the pointer targets the characters; the u32 length sits just before them.</summary>
    public string GmString(int offset) => Encoding.UTF8.GetString(_bytes, offset, (int)U32(offset - 4));

    public ReadOnlySpan<byte> Span(int offset, int length) => _bytes.AsSpan(offset, length);

    internal Stream OpenRead(int offset) => new MemoryStream(_bytes, offset, _bytes.Length - offset, writable: false);
}

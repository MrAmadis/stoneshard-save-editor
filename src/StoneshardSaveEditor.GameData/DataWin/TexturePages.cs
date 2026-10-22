using ICSharpCode.SharpZipLib.BZip2;

namespace StoneshardSaveEditor.GameData;

/// <summary>
/// Decodes texture pages from the <c>TXTR</c> chunk on demand, keeping the most recent ones in memory.
/// Stoneshard stores every page as "2zoq": BZip2-compressed GameMaker QOI.
/// </summary>
public sealed class TexturePages(DataWinFile file)
{
    private const int Capacity = 8;
    private readonly LinkedList<(int Index, Rgba32Image Image)> _recent = new();
    private readonly Lock _lock = new();

    /// <summary>How many pages have been decoded so far (each costs ~100 ms).</summary>
    public int DecodeCount { get; private set; }

    public Rgba32Image Page(int index)
    {
        lock (_lock)
        {
            for (var node = _recent.First; node is not null; node = node.Next)
            {
                if (node.Value.Index != index)
                    continue;
                _recent.Remove(node);
                _recent.AddFirst(node);
                return node.Value.Image;
            }

            var image = Decode(index);
            DecodeCount++;
            _recent.AddFirst((index, image));
            if (_recent.Count > Capacity)
                _recent.RemoveLast();
            return image;
        }
    }

    private Rgba32Image Decode(int index)
    {
        var (offset, _) = file.Chunk("TXTR");
        var count = (int)file.U32(offset);
        if (index < 0 || index >= count)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"data.win has {count} texture pages");
        var entry = (int)file.U32(offset + 4 + 4 * index);
        var blob = (int)file.U32(entry + 24);
        var magic = file.Span(blob, 4);

        if (magic.SequenceEqual("fioq"u8))
            return GmQoi.Decode(file.Span(blob, 12 + (int)file.U32(blob + 8)));

        if (magic.SequenceEqual("2zoq"u8))
        {
            var bzStart = file.Span(blob, 32).IndexOf("BZh"u8);
            if (bzStart < 0)
                throw new InvalidDataException($"Texture page {index} has no BZip2 stream");
            using var bz = new BZip2InputStream(file.OpenRead(blob + bzStart));
            using var qoi = new MemoryStream();
            bz.CopyTo(qoi);
            return GmQoi.Decode(qoi.GetBuffer().AsSpan(0, (int)qoi.Length));
        }

        throw new NotSupportedException($"Texture page {index} uses an unsupported format");
    }
}

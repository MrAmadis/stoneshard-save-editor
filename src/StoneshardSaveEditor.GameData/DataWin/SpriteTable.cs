namespace StoneshardSaveEditor.GameData;

/// <summary>Where a sprite's first frame sits on its texture page and how it is placed in the sprite's bounds.</summary>
public sealed record SpriteFrame(
    int SourceX, int SourceY, int SourceW, int SourceH,
    int TargetX, int TargetY, int TargetW, int TargetH,
    int BoundW, int BoundH, int Texture);

public sealed record SpriteInfo(string Name, int Width, int Height, SpriteFrame? FirstFrame);

/// <summary>The <c>SPRT</c> chunk: every sprite's name, size and frame placements (via <c>TPAG</c>).</summary>
public sealed class SpriteTable
{
    private readonly DataWinFile _file;
    private readonly int[] _records;
    private readonly string[] _names;
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);
    private readonly SpriteInfo?[] _cache;

    public SpriteTable(DataWinFile file)
    {
        _file = file;
        var (offset, _) = file.Chunk("SPRT");
        var count = (int)file.U32(offset);
        _records = new int[count];
        _names = new string[count];
        _cache = new SpriteInfo?[count];
        for (var i = 0; i < count; i++)
        {
            _records[i] = (int)file.U32(offset + 4 + 4 * i);
            _names[i] = file.GmString((int)file.U32(_records[i]));
            _byName.TryAdd(_names[i], i);
        }
    }

    public int Count => _records.Length;

    public IEnumerable<string> Names => _names;

    public SpriteInfo? Get(string name) => _byName.TryGetValue(name, out var i) ? ByIndex(i) : null;

    public SpriteInfo ByIndex(int i) => _cache[i] ??= Read(i);

    private SpriteInfo Read(int i)
    {
        var e = _records[i];
        var width = (int)_file.U32(e + 4);
        var height = (int)_file.U32(e + 8);
        return new SpriteInfo(_names[i], width, height, ReadFirstFrame(e));
    }

    private SpriteFrame? ReadFirstFrame(int e)
    {
        // GameMaker Studio 2 sprites mark their extended header with -1 at +56, then version and type.
        if (_file.I32(e + 56) != -1 || _file.U32(e + 64) != 0)
            return null;
        var version = _file.U32(e + 60);
        var countOffset = e + 76 + (version >= 2 ? 4 : 0) + (version >= 3 ? 4 : 0);
        if (_file.U32(countOffset) == 0)
            return null;
        var t = (int)_file.U32(countOffset + 4);
        int H(int k) => _file.U16(t + 2 * k);
        return new SpriteFrame(H(0), H(1), H(2), H(3), H(4), H(5), H(6), H(7), H(8), H(9), H(10));
    }
}

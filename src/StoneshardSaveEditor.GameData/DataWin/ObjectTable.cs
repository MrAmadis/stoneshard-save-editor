namespace StoneshardSaveEditor.GameData;

/// <summary>The <c>OBJT</c> chunk: game object names and the sprite each one draws with.</summary>
public sealed class ObjectTable
{
    private readonly Dictionary<string, string?> _spriteByObject = new(StringComparer.Ordinal);

    public ObjectTable(DataWinFile file, SpriteTable sprites)
    {
        var (offset, _) = file.Chunk("OBJT");
        var count = (int)file.U32(offset);
        for (var i = 0; i < count; i++)
        {
            var record = (int)file.U32(offset + 4 + 4 * i);
            var name = file.GmString((int)file.U32(record));
            var spriteIndex = file.I32(record + 4);
            _spriteByObject.TryAdd(name, spriteIndex >= 0 && spriteIndex < sprites.Count ? sprites.ByIndex(spriteIndex).Name : null);
        }
    }

    public IEnumerable<string> Names => _spriteByObject.Keys;

    public bool Exists(string obj) => _spriteByObject.ContainsKey(obj);

    public string? SpriteOf(string obj) => _spriteByObject.GetValueOrDefault(obj);
}

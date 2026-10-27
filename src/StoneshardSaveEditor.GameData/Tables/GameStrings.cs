namespace StoneshardSaveEditor.GameData;

/// <summary>
/// The game's string literals, which hold its data tables and text, as consecutive NUL-terminated strings: the layout
/// <see cref="ExeTables"/> and <see cref="Localization"/> read. The native (YYC) build compiles them into
/// <c>StoneShard.exe</c>. The VM build (Steam's <c>modbranch</c>, which mods need) ships the stock runner as its exe and
/// keeps the game in <c>data.win</c>: bytecode in CODE, literals in STRG.
/// </summary>
public static class GameStrings
{
    public static byte[] Load(string gameDir, DataWinFile dataWin) =>
        dataWin.HasChunk("CODE") ? FromStrg(dataWin) : File.ReadAllBytes(Path.Combine(gameDir, "StoneShard.exe"));

    /// <summary>
    /// STRG is a u32 count and a pointer per string; each points at a u32 length, the characters and a NUL.
    /// The VM compiler pushes an array literal's elements last-first, so each table sits in STRG bottom-up with its
    /// header last. Reversing every run of strings with the same number of <c>;</c> restores the exe's order.
    /// </summary>
    internal static byte[] FromStrg(DataWinFile dataWin)
    {
        var (offset, _) = dataWin.Chunk("STRG");
        var count = (int)dataWin.U32(offset);
        var strings = new (int At, int Length, int Separators)[count];
        for (var i = 0; i < count; i++)
        {
            var at = (int)dataWin.U32(offset + 4 + 4 * i) + 4;
            var length = (int)dataWin.U32(at - 4);
            strings[i] = (at, length, dataWin.Span(at, length).Count((byte)';'));
        }

        var pool = new MemoryStream();
        for (var start = 0; start < count;)
        {
            var end = start + 1;
            while (end < count && strings[end].Separators == strings[start].Separators)
                end++;
            for (var i = end - 1; i >= start; i--)
            {
                pool.WriteByte(0);
                pool.Write(dataWin.Span(strings[i].At, strings[i].Length));
            }
            start = end;
        }
        pool.WriteByte(0);
        return pool.ToArray();
    }
}

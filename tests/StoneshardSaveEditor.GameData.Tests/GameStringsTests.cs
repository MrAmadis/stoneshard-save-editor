using System.Text;
using StoneshardSaveEditor.GameData;

namespace StoneshardSaveEditor.Tests;

public class GameStringsTests
{
    [Fact]
    public void Strg_pool_holds_the_data_win_strings()
    {
        var pool = GameStrings.FromStrg(GameFiles.DataWin);
        Assert.True(pool.AsSpan().IndexOf("\0o_inv_salve\0"u8) >= 0);
    }

    [Fact]
    public void Vm_build_reads_tables_and_text_from_data_win()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "StoneShard.exe"), "runner");
        var text = new string(';', 11);
        File.WriteAllBytes(Path.Combine(dir, "data.win"), VmDataWin(
            "junk", "y;3;4", "// note;;", "x;1;2", "h;a;b", "gml_Script_table_h",
            "k;;Its description" + text, "k;;Name" + text, "gml_Script_table_text"));

        var strings = GameStrings.Load(dir, DataWinFile.Load(Path.Combine(dir, "data.win")));

        var table = ExeTables.ReadTable(strings, "h;a;b");
        Assert.Equal(new[] { "x", "y" }, table.Rows.Select(r => r.Key));
        Assert.Equal("4", table.Find("y")!["b"]);
        Assert.Equal("Name", Localization.Parse(strings).English("k"));
    }

    /// <summary>
    /// A VM build's data.win: bytecode in CODE, and STRG's length-prefixed strings padded to 4 bytes, where each table
    /// sits bottom-up with its header last, followed by its script's name.
    /// </summary>
    private static byte[] VmDataWin(params string[] strings)
    {
        var body = new MemoryStream();
        var w = new BinaryWriter(body);
        w.Write("CODE"u8);
        w.Write(4);
        w.Write(0);
        w.Flush();

        var strg = 8 + (int)body.Length + 8;
        var at = strg + 4 + 4 * strings.Length;
        var entries = new MemoryStream();
        var pointers = new List<int>();
        foreach (var s in strings)
        {
            pointers.Add(at + (int)entries.Length);
            var bytes = Encoding.UTF8.GetBytes(s);
            entries.Write(BitConverter.GetBytes(bytes.Length));
            entries.Write(bytes);
            entries.WriteByte(0);
            while (entries.Length % 4 != 0)
                entries.WriteByte(0);
        }
        w.Write("STRG"u8);
        w.Write(4 + 4 * strings.Length + (int)entries.Length);
        w.Write(strings.Length);
        pointers.ForEach(w.Write);
        w.Write(entries.ToArray());
        w.Flush();

        return [.. "FORM"u8, .. BitConverter.GetBytes((int)body.Length), .. body.ToArray()];
    }
}

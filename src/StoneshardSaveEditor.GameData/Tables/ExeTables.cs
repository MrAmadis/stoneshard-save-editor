using System.Text;

namespace StoneshardSaveEditor.GameData;

/// <summary>
/// The game's data tables, found in its <see cref="GameStrings"/> as consecutive NUL-terminated strings:
/// a header row followed by rows with the same number of <c>;</c> separators.
/// </summary>
public sealed class ExeTables
{
    private const string WeaponsHeader = "name;Tier;id;Slot;Subtype;altGrip;rarity;Mat;Price;Markup;MaxDuration;Rng;";
    private const string ArmorHeader = "name;Tier;id;Slot;class;rarity;Mat;Price;Markup;MaxDuration;DEF;";
    private const string ItemsStatsHeader = "id;;Price;EffPrice;tier;Cat;Subcat;Material;Weight;";
    private const string SkillsStatsHeader = ";Object;Target;Range;KD;MP;";

    private ExeTables(GameTable weapons, GameTable armor, GameTable itemsStats, GameTable skillsStats)
    {
        Weapons = weapons;
        Armor = armor;
        ItemsStats = itemsStats;
        SkillsStats = skillsStats;
    }

    public GameTable Weapons { get; }
    public GameTable Armor { get; }
    public GameTable ItemsStats { get; }
    public GameTable SkillsStats { get; }

    public static ExeTables Load(string exePath) => Parse(File.ReadAllBytes(exePath));

    public static ExeTables Parse(byte[] exe) => new(
        ReadTable(exe, WeaponsHeader),
        ReadTable(exe, ArmorHeader),
        ReadTable(exe, ItemsStatsHeader),
        ReadTable(exe, SkillsStatsHeader));

    internal static GameTable ReadTable(byte[] exe, string headerPrefix)
    {
        var needle = Encoding.UTF8.GetBytes("\0" + headerPrefix);
        var at = exe.AsSpan().IndexOf(needle);
        if (at < 0)
            throw new InvalidDataException($"Game table starting '{headerPrefix}' was not found");

        var pos = at + 1;
        var header = NextString(exe, ref pos);
        var separators = header.Count(c => c == ';');
        var rows = new List<string[]>();
        while (true)
        {
            while (pos < exe.Length && exe[pos] == 0)
                pos++;
            if (pos >= exe.Length)
                break;
            var row = NextString(exe, ref pos);
            if (row.Count(c => c == ';') != separators)
                break;
            if (row.StartsWith("//", StringComparison.Ordinal) || row.StartsWith('[') || row.StartsWith(';'))
                continue;
            rows.Add(row.Split(';'));
        }
        return new GameTable(header.Split(';'), rows);
    }

    private static string NextString(byte[] exe, ref int pos)
    {
        var end = Array.IndexOf(exe, (byte)0, pos);
        if (end < 0)
            end = exe.Length;
        var s = Encoding.UTF8.GetString(exe, pos, end - pos);
        pos = end;
        return s;
    }
}

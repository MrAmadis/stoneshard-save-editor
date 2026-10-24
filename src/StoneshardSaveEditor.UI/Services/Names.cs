using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using StoneshardSaveEditor.GameData;

namespace StoneshardSaveEditor.UI.Services;

/// <summary>Turns the game's internal keys into names people read.</summary>
public static partial class Names
{
    public static string SlotDisplayName(string folder)
    {
        var m = SlotPattern().Match(folder);
        if (!m.Success)
            return folder;
        return m.Groups[1].Value switch
        {
            "exitsave" => "Exit save",
            "save" => $"Save {m.Groups[2].Value}",
            _ => $"Autosave {m.Groups[2].Value}",
        };
    }

    /// <summary>A treatise volume as the game writes it: 1 → "I", 4 → "IV".</summary>
    public static string Roman(int volume) =>
        volume is >= 1 and <= 9 ? new[] { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" }[volume - 1] : volume.ToString(CultureInfo.InvariantCulture);

    /// <summary>Names joined for a sentence: "A", "A and B", "A, B and C".</summary>
    public static string List(IReadOnlyList<string> names) =>
        names.Count <= 1 ? string.Concat(names) : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";

    /// <summary>The banner for items that came off with no room in the bag: "A had no room and was removed".</summary>
    public static string NoRoom(IReadOnlyList<string> names) =>
        $"{List(names)} had no room and {(names.Count == 1 ? "was" : "were")} removed";

    /// <summary>Conditions for a sentence, each met by any one of its names: "A and B", "A or B, and C".</summary>
    public static string Conditions(IReadOnlyList<IReadOnlyList<string>> conditions)
    {
        var parts = conditions.Select(Either).ToList();
        return conditions.Any(c => c.Count > 1) ? string.Join(", and ", parts) : List(parts);
    }

    private static string Either(IReadOnlyList<string> names) =>
        names.Count <= 1 ? string.Concat(names) : $"{string.Join(", ", names.Take(names.Count - 1))} or {names[^1]}";

    /// <summary>A label for an item catalog category, which is a table's item type ("2haxe", "Head", "food").</summary>
    public static string Category(string category) => category switch
    {
        "" => "Other",
        "2haxe" => "Two-handed axe",
        "2hmace" => "Two-handed mace",
        "2hsword" => "Two-handed sword",
        "2hStaff" => "Staff",
        _ => char.ToUpperInvariant(category[0]) + category[1..],
    };

    /// <summary>The game's English name for a key when it has one, otherwise the key split into words.</summary>
    public static string Pretty(string key, Localization? localization) =>
        localization?.English(key) ?? SplitWords(key);

    private static string SplitWords(string key)
    {
        var sb = new StringBuilder(key.Length + 4);
        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            if (c == '_')
            {
                sb.Append(' ');
                continue;
            }
            if (char.IsUpper(c) && i > 0 && char.IsLower(key[i - 1]))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    [GeneratedRegex("^(exitsave|save|autosave)_(\\d+)$")]
    private static partial Regex SlotPattern();
}

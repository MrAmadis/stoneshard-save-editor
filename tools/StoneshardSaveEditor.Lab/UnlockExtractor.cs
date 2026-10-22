using System.Text.Json;
using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Lab;

/// <summary>Turns "before/after reading treatises" save pairs into <c>tree-unlocks.json</c> entries.</summary>
public static class UnlockExtractor
{
    public static IReadOnlyList<string> NewlyUnlocked(GmObject before, GmObject after)
    {
        var wasLocked = SkillsEditor.ReadFrom(before).Where(s => s.Locked).Select(s => s.Object).ToHashSet();
        return SkillsEditor.ReadFrom(after).Where(s => !s.Locked && wasLocked.Contains(s.Object)).Select(s => s.Object).ToList();
    }

    /// <summary>Treatise ids (e.g. <c>pyro1</c>) that appear in <c>Books_Read</c> after but not before.</summary>
    public static IReadOnlyList<string> NewlyReadTreatises(GmObject before, GmObject after)
    {
        static IEnumerable<string> Books(GmObject root) =>
            ((root["characterDataMap"] as GmObject)?["Books_Read"] as GmArray)?.Items.OfType<GmString>().Select(s => s.Value) ?? [];
        var old = Books(before).ToHashSet();
        return Books(after)
            .Where(b => !old.Contains(b) && b.StartsWith("treatise_", StringComparison.Ordinal))
            .Select(b => b["treatise_".Length..])
            .ToList();
    }

    /// <summary>
    /// Groups skills under <c>&lt;treatise prefix&gt;&lt;volume&gt;</c>, e.g. pyro1. With <paramref name="onlyRead"/>, keeps only
    /// treatises that were actually read: the game also unlocks skills on its own when it recalculates a tree on load.
    /// </summary>
    public static Dictionary<string, string[]> GroupByTreatise(
        IEnumerable<string> skills, Func<string, string?> treatisePrefixOf, int volume, IReadOnlyCollection<string>? onlyRead = null) =>
        skills.Select(s => (Skill: s, Prefix: treatisePrefixOf(s)))
            .Where(x => x.Prefix is not null)
            .GroupBy(x => $"{x.Prefix}{volume}")
            .Where(g => onlyRead is null || onlyRead.Contains(g.Key))
            .ToDictionary(g => g.Key, g => g.Select(x => x.Skill).ToArray());

    public static void Merge(string jsonPath, Dictionary<string, string[]> entries)
    {
        var existing = File.Exists(jsonPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(jsonPath)) ?? []
            : [];
        foreach (var (key, skills) in entries)
            existing[key] = existing.GetValueOrDefault(key, []).Concat(skills).Distinct().Order(StringComparer.Ordinal).ToArray();
        var sorted = existing.OrderBy(e => e.Key, StringComparer.Ordinal).ToDictionary(e => e.Key, e => e.Value);
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(sorted, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.GameData;

public sealed record SkillInfo(string Object, string DisplayName, string Sprite, bool Passive, string TreeId);

public sealed record SkillTree(string Id, string DisplayName, string? TreatisePrefix, IReadOnlyList<SkillInfo> Skills);

public sealed record SkillPosition(int Col, int Row, string[] Requires);

public sealed record TreeLayout(IReadOnlyDictionary<string, SkillPosition> Skills);

/// <summary>
/// The skill trees: which skill belongs where, its English name and icon, plus hand-made data observed in-game
/// (<c>tree-unlocks.json</c>: what each treatise opens; <c>skill-trees.json</c>: layout and prerequisites).
/// </summary>
public sealed partial class SkillCatalog
{
    public const string BasicTree = "basic";
    private const string SurvivalTree = "survival";
    private const string ActivePrefix = "o_skill_", ActiveSuffix = "_ico", PassivePrefix = "o_pass_skill_";

    private static readonly Dictionary<string, (string Display, string? Treatise)> TreeNames = new()
    {
        ["athletic"] = ("Athletics", "athletics"),
        ["combat"] = ("Warfare", "combat"),
        ["magic_mastery"] = ("Magic Mastery", "magic"),
        ["arcanistics"] = ("Arcanistics", "arcane"),
        ["pyromancy"] = ("Pyromancy", "pyro"),
        ["geomancy"] = ("Geomancy", "geo"),
        ["electromancy"] = ("Electromancy", "electro"),
        ["ranged"] = ("Archery", "ranged"),
        ["spear"] = ("Polearms", "polearms"),
        ["staff"] = ("Staves", "staff"),
        ["survival"] = ("Survival", "survival"),
        ["sword"] = ("Swords", "sword"),
        ["axe"] = ("Axes", "axe"),
        ["mace"] = ("Maces", "mace"),
        ["dagger"] = ("Daggers", "dagger"),
        ["2hsword"] = ("Greatswords", "gsword"),
        ["2haxe"] = ("Greataxes", "greataxes"),
        ["2hmace"] = ("Greatmaces", "greatmaces"),
        ["dual"] = ("Dual Wielding", "dualwield"),
        ["shield"] = ("Shields", "shield"),
        ["armor"] = ("Armored Combat", "armor"),
        [BasicTree] = ("Basic", null),
    };

    private readonly Dictionary<string, SkillTree> _treeOf;
    private readonly Dictionary<string, string[]> _unlocks;
    private readonly Dictionary<string, TreeLayout> _layouts;

    private SkillCatalog(IReadOnlyList<SkillTree> trees, Dictionary<string, string[]> unlocks, Dictionary<string, TreeLayout> layouts)
    {
        Trees = trees;
        _treeOf = trees.SelectMany(t => t.Skills.Select(s => (s.Object, t))).ToDictionary(x => x.Object, x => x.t);
        _unlocks = unlocks;
        _layouts = layouts;
    }

    public IReadOnlyList<SkillTree> Trees { get; }

    public SkillTree TreeOf(string obj) => _treeOf[obj];

    /// <summary>Skills a closed tree's treatises unlock (all volumes together); empty until observed in-game.</summary>
    public IReadOnlyList<string> UnlockSet(string treeId) =>
        Trees.FirstOrDefault(t => t.Id == treeId)?.TreatisePrefix is { } prefix ? UnionForTreatise(_unlocks, prefix) : [];

    public TreeLayout? Layout(string treeId) => _layouts.GetValueOrDefault(treeId);

    public static IReadOnlyList<string> UnionForTreatise(IReadOnlyDictionary<string, string[]> unlocks, string prefix) =>
        unlocks.Where(e => Regex.IsMatch(e.Key, $"^{Regex.Escape(prefix)}\\d+$"))
            .SelectMany(e => e.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The save lists every skill grouped by tree: a block of passives followed by its actives. A block's tree is the
    /// <c>Branch</c> of its actives in the game's skill table; the only block marked "none" is Survival. The innate
    /// skills, scattered through the list, form the Basic tree.
    /// </summary>
    public static SkillCatalog Build(GmObject saveRoot, ExeTables tables, Localization localization, ObjectTable objects)
    {
        var list = ((saveRoot["skillsDataMap"] as GmObject)?["skillsAllDataList"] as GmArray)?.Items
                   ?? throw new InvalidDataException("This save has no skill list");
        var names = list.Where((_, i) => i % 5 == 0).OfType<GmString>().Select(s => s.Value).ToList();

        var byKey = new Dictionary<string, TableRow>(StringComparer.OrdinalIgnoreCase);
        var byObject = new Dictionary<string, TableRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in tables.SkillsStats.Rows)
        {
            byKey.TryAdd(row.Key, row);
            byObject.TryAdd(row["Object"], row);
        }
        TableRow? ActiveRow(string stem) => byKey.GetValueOrDefault(stem) ?? byObject.GetValueOrDefault("o_" + stem);

        var blocks = new List<List<string>>();
        var current = new List<string>();
        var previousWasActive = false;
        foreach (var name in names.Where(n => !SkillsEditor.Innate.Contains(n)))
        {
            var active = name.StartsWith(ActivePrefix, StringComparison.Ordinal);
            if (!active && previousWasActive && current.Count > 0)
            {
                blocks.Add(current);
                current = [];
            }
            current.Add(name);
            previousWasActive = active;
        }
        if (current.Count > 0)
            blocks.Add(current);

        SkillInfo Info(string obj, string treeId)
        {
            var passive = obj.StartsWith(PassivePrefix, StringComparison.Ordinal);
            var stem = passive ? obj[PassivePrefix.Length..] : obj[ActivePrefix.Length..^ActiveSuffix.Length];
            var english = passive ? localization.English(stem) : ActiveRow(stem) is { } r ? localization.English(r.Key) : null;
            return new SkillInfo(obj, english ?? TitleCase(stem), objects.SpriteOf(obj) ?? GameItemShapes.Missing, passive, treeId);
        }

        var trees = new List<SkillTree>();
        foreach (var block in blocks)
        {
            var branch = block.Where(n => n.StartsWith(ActivePrefix, StringComparison.Ordinal))
                .Select(n => ActiveRow(n[ActivePrefix.Length..^ActiveSuffix.Length])?["Branch"])
                .Where(b => !string.IsNullOrEmpty(b))
                .GroupBy(b => b!)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? "none";
            var id = branch == "none" ? SurvivalTree : branch;
            trees.Add(Tree(id, block.Select(n => Info(n, id)).ToList()));
        }
        trees.Add(Tree(BasicTree, names.Where(SkillsEditor.Innate.Contains).Select(n => Info(n, BasicTree)).ToList()));

        return new SkillCatalog(trees, LoadUnlocks(), LoadLayouts());
    }

    private static SkillTree Tree(string id, IReadOnlyList<SkillInfo> skills)
    {
        var (display, treatise) = TreeNames.GetValueOrDefault(id, (TitleCase(id), null));
        return new SkillTree(id, display, treatise, skills);
    }

    private static string TitleCase(string stem) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(stem.Replace('_', ' ').ToLowerInvariant());

    private static Dictionary<string, string[]> LoadUnlocks() =>
        JsonSerializer.Deserialize<Dictionary<string, string[]>>(Resource("tree-unlocks.json")) ?? [];

    private static Dictionary<string, TreeLayout> LoadLayouts()
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, LayoutEntry>>>(Resource("skill-trees.json")) ?? [];
        return raw.ToDictionary(
            t => t.Key,
            t => new TreeLayout(t.Value.ToDictionary(s => s.Key, s => new SkillPosition(s.Value.Col, s.Value.Row, s.Value.Requires ?? []))));
    }

    private static Stream Resource(string name) =>
        typeof(SkillCatalog).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Embedded resource {name} is missing");

    private sealed record LayoutEntry(
        [property: System.Text.Json.Serialization.JsonPropertyName("col")] int Col,
        [property: System.Text.Json.Serialization.JsonPropertyName("row")] int Row,
        [property: System.Text.Json.Serialization.JsonPropertyName("requires")] string[]? Requires);
}

using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;
using StoneshardSaveEditor.GameData;

namespace StoneshardSaveEditor.Tests;

/// <summary>skill-trees.json, transcribed from the owner's screenshots of every tree with all skills learned (session B).</summary>
public class SkillTreesDataTests
{
    private static readonly Lazy<SkillCatalog> Catalog = new(() =>
    {
        var file = Path.Combine(Fixtures.RealSavesRoot, "character_2", "exitsave_1", "data.sav");
        var root = (GmObject)GmJson.Parse(SaveCodec.Decode(File.ReadAllBytes(file)).Json);
        return SkillCatalog.Build(root, GameFiles.Tables, GameFiles.Localization, GameFiles.Objects);
    });

    private static IEnumerable<SkillTree> TreesWithTreatise()
    {
        _ = GameFiles.Objects; // skip without the game
        return Catalog.Value.Trees.Where(t => t.TreatisePrefix is not null);
    }

    [Fact]
    public void Every_tree_has_positions_for_all_its_skills()
    {
        var trees = TreesWithTreatise().ToList();
        Assert.Equal(21, trees.Count);
        foreach (var tree in trees)
        {
            var layout = Catalog.Value.Layout(tree.Id);
            Assert.True(layout is not null, $"{tree.Id} has no layout");
            var skills = tree.Skills.Select(s => s.Object).ToHashSet();
            Assert.True(skills.SetEquals(layout.Skills.Keys), $"{tree.Id}: layout and tree skills differ");
            foreach (var (obj, at) in layout.Skills)
            {
                Assert.All(at.Requires, r => Assert.True(skills.Contains(r), $"{tree.Id}/{obj} requires {r} from another tree"));
                Assert.All(at.Requires, r => Assert.True(layout.Skills[r].Row < at.Row, $"{tree.Id}/{obj} requires {r}, which is not above it"));
            }
            var cells = layout.Skills.Values.Select(p => (p.Row, p.Col)).ToList();
            Assert.Equal(cells.Count, cells.Distinct().Count());
        }
    }

    [Fact]
    public void First_tier_matches_tree_unlocks()
    {
        foreach (var tree in TreesWithTreatise())
        {
            var unlocks = Catalog.Value.UnlockSet(tree.Id);
            if (unlocks.Count == 0)
                continue;
            var firstTier = Catalog.Value.Layout(tree.Id)!.Skills.Where(s => s.Value.Requires.Length == 0).Select(s => s.Key);
            Assert.True(firstTier.ToHashSet().SetEquals(unlocks), $"{tree.Id}: first tier differs from the Volume I unlocks");
        }
    }

    [Fact]
    public void Rules_are_available_once_every_tree_has_a_layout()
    {
        _ = TreesWithTreatise();
        var rules = Catalog.Value.Rules();

        Assert.NotNull(rules);
        var state = new[] { "o_skill_wide_cut_ico", "o_pass_skill_shieldbreaker", "o_skill_dismember_ico" }
            .Select(o => new Core.Editing.SkillState(o, Learned: true, Locked: false))
            .Append(new Core.Editing.SkillState("o_pass_skill_fatal_hit", Learned: false, Locked: true))
            .ToList();
        Assert.True(rules.CanLearn("o_pass_skill_fatal_hit", state));
        Assert.Equal("o_pass_skill_fatal_hit", rules.BlockingDependent("o_skill_wide_cut_ico",
            [.. state.Take(3), new Core.Editing.SkillState("o_pass_skill_fatal_hit", Learned: true, Locked: false)]));
    }
}

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
    public void Each_treatise_volume_opens_one_row()
    {
        // Session B: Pyromancy II, III and IV each unlocked exactly one more row; Volume I matches the first row everywhere.
        var trees = TreesWithTreatise().ToList();
        Assert.All(trees, t => Assert.Equal(t.Id == "survival" ? 2 : Catalog.Value.Layout(t.Id)!.Skills.Values.Max(p => p.Row) + 1,
            Catalog.Value.Volumes(t.Id)));
        var pyromancy = Catalog.Value.Layout("pyromancy")!.Skills;
        foreach (var volume in Enumerable.Range(1, 4))
            Assert.True(Catalog.Value.TierSkills("pyromancy", volume).ToHashSet()
                .SetEquals(pyromancy.Where(s => s.Value.Row == volume - 1).Select(s => s.Key)), $"Pyromancy {volume}");
        Assert.Equal(new[] { "o_pass_skill_feed_the_flames", "o_pass_skill_scorch", "o_skill_flame_wave_ico", "o_skill_magma_rain_ico" },
            Catalog.Value.TierSkills("pyromancy", 2).Order());
        // Survival has two volumes for three rows: its first row is always open.
        Assert.Null(Catalog.Value.VolumeOf("o_pass_skill_make_a_halt"));
        Assert.Equal(2, Catalog.Value.VolumeOf(Catalog.Value.Layout("survival")!.Skills.First(s => s.Value.Row == 2).Key));
        Assert.Equal(3, Catalog.Value.VolumeOf("o_skill_killing_swing_ico")); // Make Space, Greataxes' third row
    }

    [Fact]
    public void Rules_are_available_once_every_tree_has_a_layout()
    {
        _ = TreesWithTreatise();
        var rules = Catalog.Value.Rules();

        Assert.NotNull(rules);
        var state = new[] { "o_skill_wide_cut_ico", "o_pass_skill_shieldbreaker", "o_skill_dismember_ico" }
            .Select(o => new Core.Editing.SkillState(o, Learned: true, Locked: false))
            .Append(new Core.Editing.SkillState("o_pass_skill_fatal_hit", Learned: false, Locked: false))
            .ToList();
        Assert.True(rules.CanLearn("o_pass_skill_fatal_hit", state));
        Assert.Equal("o_pass_skill_fatal_hit", rules.BlockingDependent("o_skill_wide_cut_ico",
            [.. state.Take(3), new Core.Editing.SkillState("o_pass_skill_fatal_hit", Learned: true, Locked: false)]));
    }
}

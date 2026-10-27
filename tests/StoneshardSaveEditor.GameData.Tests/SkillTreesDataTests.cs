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
                Assert.All(at.Requires.SelectMany(c => c), r => Assert.True(skills.Contains(r), $"{tree.Id}/{obj} requires {r} from another tree"));
                Assert.All(at.Requires.SelectMany(c => c), r => Assert.True(layout.Skills[r].Row < at.Row, $"{tree.Id}/{obj} requires {r}, which is not above it"));
            }
            var cells = layout.Skills.Values.Select(p => (p.Row, p.Col)).ToList();
            Assert.Equal(cells.Count, cells.Distinct().Count());
        }
    }

    [Fact]
    public void Unlocking_a_tree_opens_its_first_tier()
    {
        foreach (var tree in TreesWithTreatise())
        {
            var unlocks = Catalog.Value.UnlockSet(tree.Id);
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

    [Theory]
    // Session D, every treatise read: what the owner learned, in order, and what that made learnable.
    [InlineData("shield", "Breakthrough", "Shield Bash", true)]
    [InlineData("staff", "Hail of Blows", "Now or Never", true)]
    [InlineData("staff", "Hail of Blows", "Unwavering Stance", false)]
    [InlineData("staff", "Hail of Blows, Step Aside!", "Unwavering Stance", true)]
    [InlineData("staff", "Hail of Blows, Step Aside!", "Triumph", true)]
    [InlineData("spear", "Precise Hits", "Pikeman Stance", true)]
    [InlineData("spear", "Precise Hits, Pikeman Stance", "One at a Time!", true)]
    [InlineData("spear", "Precise Hits, Pikeman Stance", "Regroup", false)]
    [InlineData("spear", "Precise Hits, Nail Down, Impaling Lunge, No Retreat, Pikeman Stance, Wounding Spearhead, Determination", "Regroup", true)]
    [InlineData("athletic", "Disengage, Dash", "Elusiveness", true)]
    [InlineData("2haxe", "Hooking Chop, Shieldbreaker", "Fatal Strike", false)]
    [InlineData("sword", "Cleaving Strike", "Gloat", true)]
    // Session E.
    [InlineData("axe", "Mutilating Lunge, Ferocity, Cut Through", "Reprisal", false)]
    [InlineData("axe", "Mutilating Lunge, Ferocity, Cut Through, No Mercy", "Reprisal", true)]
    [InlineData("2hsword", "Revanche, Feast of Steel", "Taste of Victory", false)]
    [InlineData("2hsword", "Revanche, Feast of Steel, Hewing Strike, Recklessness", "Taste of Victory", true)]
    [InlineData("armor", "Brace for Impact!, Flexible Defense", "Battering Ram", true)]
    [InlineData("arcanistics", "Dimensional Shift", "Schism", true)]
    [InlineData("arcanistics", "Dimensional Shift", "Transcendental Anchoring", true)]
    [InlineData("arcanistics", "Dimensional Shift", "Time Echo", false)]
    [InlineData("arcanistics", "Dimensional Shift", "Aether Shield", false)]
    [InlineData("arcanistics", "Dimensional Shift, Arcane Might", "Aether Shield", true)]
    // Session F.
    [InlineData("2hsword", "Hewing Strike, Recklessness, Revanche, Feast of Steel, Arc Cleave", "Heroic Charge", false)]
    [InlineData("2hsword", "Hewing Strike, Recklessness, Revanche, Feast of Steel, Arc Cleave, Parry, Fight to the Death", "Heroic Charge", true)]
    [InlineData("2hsword", "Parry, Fight to the Death", "Courage", true)]
    [InlineData("2hmace", "Keep Them Coming, Mighty Swing, Unstoppable Force", "Striker Stance", false)]
    [InlineData("2hmace", "Keep Them Coming, Mighty Swing, Unstoppable Force, Unbalance", "Striker Stance", true)]
    [InlineData("electromancy", "Chain Lightning, Unlimited Power, Conductivity", "Chain Reaction", false)]
    [InlineData("electromancy", "Chain Lightning, Unlimited Power, Conductivity, Ball Lightning", "Chain Reaction", true)]
    [InlineData("arcanistics", "Schism, Transcendental Anchoring", "Mana Crystal", true)]
    [InlineData("arcanistics", "Schism, Aether Shield", "Mana Crystal", false)]
    [InlineData("arcanistics", "Transcendental Anchoring", "Stasis", true)]
    [InlineData("arcanistics", "Aether Shield", "Stasis", true)]
    [InlineData("survival", "Pathfinder", "Huntmaster", false)]
    [InlineData("survival", "Resourcefulness", "Huntmaster", true)]
    [InlineData("survival", "Austerity", "Will to Survive", false)]
    [InlineData("survival", "First Aid", "Will to Survive", true)]
    public void Lines_decide_what_is_learnable_once_every_book_is_read(string treeId, string learned, string skill, bool learnable)
    {
        var tree = TreesWithTreatise().Single(t => t.Id == treeId);
        var names = learned.Split(", ").ToHashSet();
        string Obj(string name) => tree.Skills.Single(s => s.DisplayName == name).Object;
        Assert.All(names, n => Obj(n)); // every name is the game's
        var state = tree.Skills.Select(s => new Core.Editing.SkillState(s.Object, names.Contains(s.DisplayName), Locked: false)).ToList();

        Assert.Equal(learnable, Catalog.Value.Rules()!.CanLearn(Obj(skill), state));
    }
}

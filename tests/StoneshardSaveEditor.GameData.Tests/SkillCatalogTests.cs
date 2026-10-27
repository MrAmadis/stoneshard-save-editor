using System.Text.Json;
using System.Text.RegularExpressions;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;
using StoneshardSaveEditor.GameData;

namespace StoneshardSaveEditor.Tests;

public class SkillCatalogTests
{
    private static readonly Lazy<SkillCatalog> Catalog = new(() =>
    {
        var file = Path.Combine(Fixtures.RealSavesRoot, "character_2", "exitsave_1", "data.sav");
        var root = (GmObject)GmJson.Parse(SaveCodec.Decode(File.ReadAllBytes(file)).Json);
        return SkillCatalog.Build(root, GameFiles.Tables, GameFiles.Localization, GameFiles.Objects);
    });

    private static SkillCatalog Skills
    {
        get
        {
            _ = GameFiles.Objects; // skip without the game
            return Catalog.Value;
        }
    }

    [Fact]
    public void Trees_cover_all_232_skills()
    {
        var trees = Skills.Trees;
        Assert.Equal(22, trees.Count);
        Assert.Equal(21, trees.Count(t => t.TreatisePrefix is not null));
        Assert.Equal(232, trees.Sum(t => t.Skills.Count));
        Assert.Equal(232, trees.SelectMany(t => t.Skills).Select(s => s.Object).Distinct().Count());
        Assert.Equal(new[] { "athletic", "arcanistics", "geomancy" }, trees.Take(3).Select(t => t.Id));
    }

    [Theory]
    [InlineData("o_skill_wide_cut_ico", "2haxe")]
    [InlineData("o_pass_skill_shieldbreaker", "2haxe")]
    [InlineData("o_skill_first_aid_ico", "survival")]
    [InlineData("o_skill_torch_strike_ico", "basic")]
    [InlineData("o_skill_butchering_ico", "basic")]
    [InlineData("o_skill_flame_ring_ico", "pyromancy")]
    [InlineData("o_skill_execution_ico", "axe")]
    [InlineData("o_pass_skill_preparation", "combat")]
    public void Skills_land_in_their_tree(string obj, string tree) =>
        Assert.Equal(tree, Skills.TreeOf(obj).Id);

    [Fact]
    public void Trees_have_display_names_and_treatises()
    {
        var greataxes = Skills.Trees.Single(t => t.Id == "2haxe");
        Assert.Equal(("Greataxes", "greataxes"), (greataxes.DisplayName, greataxes.TreatisePrefix));
        Assert.Equal(("Warfare", "combat"), (Skills.Trees.Single(t => t.Id == "combat").DisplayName, Skills.Trees.Single(t => t.Id == "combat").TreatisePrefix));
        Assert.Null(Skills.Trees.Single(t => t.Id == "basic").TreatisePrefix);
        Assert.Equal(10, greataxes.Skills.Count);
    }

    [Fact]
    public void Names_and_sprites_resolve()
    {
        var all = Skills.Trees.SelectMany(t => t.Skills).ToList();
        var wideCut = all.Single(s => s.Object == "o_skill_wide_cut_ico");
        Assert.Equal(("Hooking Chop", "s_skills_wide_cut", false), (wideCut.DisplayName, wideCut.Sprite, wideCut.Passive));
        var shieldbreaker = all.Single(s => s.Object == "o_pass_skill_shieldbreaker");
        Assert.Equal(("Shieldbreaker", "s_passive_shieldbreaker", true), (shieldbreaker.DisplayName, shieldbreaker.Sprite, shieldbreaker.Passive));
        Assert.Equal("Butchering", all.Single(s => s.Object == "o_skill_butchering_ico").DisplayName);
        Assert.All(all, s => Assert.False(string.IsNullOrWhiteSpace(s.DisplayName)));
        Assert.All(all, s => Assert.StartsWith("s_", s.Sprite));
    }

    [Fact]
    public void Unlock_sets_and_tiers_match_every_treatise_read_in_game()
    {
        // tree-unlocks.json: what reading each book unlocked in-game (sessions A4 and B), keyed like "pyro1" for Pyromancy I.
        var observed = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "tree-unlocks.json")))!;
        Assert.Equal(14, observed.Count);
        foreach (var (book, unlocked) in observed)
        {
            var match = Regex.Match(book, @"^(\D+)(\d+)$");
            var tree = Skills.Trees.Single(t => t.TreatisePrefix == match.Groups[1].Value);
            var volume = int.Parse(match.Groups[2].Value);
            Assert.True(Skills.TierSkills(tree.Id, volume).ToHashSet().SetEquals(unlocked), $"{book}: tier differs");
            if (volume == 1)
                Assert.True(Skills.UnlockSet(tree.Id).ToHashSet().SetEquals(unlocked), $"{book}: unlock set differs");
        }
        Assert.Empty(Skills.UnlockSet("basic"));
        Assert.Throws<KeyNotFoundException>(() => Skills.TreeOf("o_skill_not_real_ico"));
    }

    [Theory]
    // Open on Velmir, so never read in-game: Volume I opens the first row, as everywhere it was read.
    [InlineData("shield", "o_pass_skill_moment_of_retribution, o_pass_skill_ultimate_resilience, o_skill_breakthrough_ico, o_skill_raise_shield_ico")]
    [InlineData("axe", "o_pass_skill_ferocity, o_pass_skill_show_no_mercy, o_skill_crippling_lunge_ico, o_skill_cut_through_ico")]
    [InlineData("mace", "o_pass_skill_dazing_strikes, o_pass_skill_moment_of_weakness, o_skill_armor_break_ico, o_skill_onslaught_ico")]
    public void Trees_never_read_in_game_unlock_their_first_row(string treeId, string firstRow) =>
        Assert.Equal(firstRow.Split(", "), Skills.UnlockSet(treeId).Order());
}

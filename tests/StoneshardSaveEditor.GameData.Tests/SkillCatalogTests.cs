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
    public void Unknown_trees_have_no_unlock_data_or_layout_yet()
    {
        Assert.Empty(Skills.UnlockSet("pyromancy"));
        Assert.Null(Skills.Layout("pyromancy"));
        Assert.Throws<KeyNotFoundException>(() => Skills.TreeOf("o_skill_not_real_ico"));
    }

    [Fact]
    public void Unlock_set_unions_all_volumes_of_a_tree()
    {
        var unlocks = new Dictionary<string, string[]>
        {
            ["pyro1"] = ["o_skill_flame_ring_ico", "o_pass_skill_pyromania"],
            ["pyro2"] = ["o_skill_melting_ray_ico", "o_skill_flame_ring_ico"],
            ["geo1"] = ["o_skill_stone_armor_ico"],
        };
        Assert.Equal(
            new[] { "o_pass_skill_pyromania", "o_skill_flame_ring_ico", "o_skill_melting_ray_ico" },
            SkillCatalog.UnionForTreatise(unlocks, "pyro").Order());
    }
}

using StoneshardSaveEditor.GameData;

namespace StoneshardSaveEditor.Tests;

public class ExeTablesTests
{
    [Fact]
    public void Weapons_table_has_guard_crossbow()
    {
        var r = GameFiles.Tables.Weapons.Find("Guard Crossbow")!;
        Assert.Equal("2", r["Tier"]);
        Assert.Equal("crossbow14", r["id"]);
        Assert.Equal("crossbow", r["Slot"]);
        Assert.Equal("40", r["MaxDuration"]);
        Assert.Equal("-55", r["Hit_Chance"]);
        Assert.Equal("25", r["Armor_Piercing"]);
        Assert.Equal("", r["Not_A_Column"]);
    }

    [Fact]
    public void Weapons_table_skips_section_and_comment_rows()
    {
        var keys = GameFiles.Tables.Weapons.Rows.Select(r => r.Key).ToList();
        Assert.DoesNotContain(keys, k => k.StartsWith("//", StringComparison.Ordinal) || k.StartsWith('['));
        Assert.Contains("Royal Huntmaster Crossbow", keys);
    }

    [Fact]
    public void Armor_table_has_road_guard_brigandine() =>
        Assert.NotNull(GameFiles.Tables.Armor.Find("Road Guard Brigandine"));

    [Fact]
    public void Items_stats_has_consumables_and_treatises()
    {
        var items = GameFiles.Tables.ItemsStats;
        Assert.Equal("metal", items.Find("salve")!["Material"]);
        Assert.Equal("medicine", items.Find("salve")!["Cat"]);
        Assert.Equal("2", items.Find("wineskin")!["Stacks"]);
        Assert.Equal("4", items.Find("treatise_pyro4")!["tier"]);
        Assert.Equal("treatise", items.Find("treatise_pyro4")!["Subcat"]);
        Assert.Equal("bag", items.Find("moneybag")!["Cat"]);
    }

    [Fact]
    public void Skills_stats_has_branches()
    {
        var skills = GameFiles.Tables.SkillsStats;
        Assert.Equal("2haxe", skills.Find("Wide_Cut")!["Branch"]);
        Assert.Equal("o_wide_cut", skills.Find("Wide_Cut")!["Object"]);
        Assert.Equal("axe", skills.Find("Execution")!["Branch"]);
    }

    [Theory]
    [InlineData("Wide_Cut", "Hooking Chop")]
    [InlineData("salve", "Healing Salve")]
    [InlineData("Road Guard Brigandine", "Road Guard Armor")]
    [InlineData("treatise_pyro1", "Pyromancy Treatise I")]
    [InlineData("Antique Copper Ring", "Ancient Copper Ring")]
    [InlineData("Splinted Boots", "Splint Boots")]
    public void English_names_resolve(string key, string english) =>
        Assert.Equal(english, GameFiles.Localization.English(key));

    [Fact]
    public void Unknown_key_has_no_english_name() =>
        Assert.Null(GameFiles.Localization.English("definitely_not_a_key"));

    [Fact]
    public void Table_parser_reads_rows_until_column_count_changes()
    {
        var exe = "junk\0h;a;b\0x;1;2\0\0\0// note;;\0y;3;4\0other\0"u8.ToArray();
        var table = ExeTables.ReadTable(exe, "h;a;b");
        Assert.Equal(new[] { "h", "a", "b" }, table.Header);
        Assert.Equal(new[] { "x", "y" }, table.Rows.Select(r => r.Key));
        Assert.Equal("4", table.Find("y")!["b"]);
    }
}

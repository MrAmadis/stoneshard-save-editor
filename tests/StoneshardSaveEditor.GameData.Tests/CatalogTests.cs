using System.Text;
using System.Text.RegularExpressions;
using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;
using StoneshardSaveEditor.GameData;

namespace StoneshardSaveEditor.Tests;

public class CatalogTests
{
    /// <summary>Values that legitimately differ between two copies of the same item (wear, ammo loaded, ownership, freshness…).</summary>
    private static readonly HashSet<string> InstanceKeys =
    [
        "Duration", "bolt", "arrow", "Colour", "key", "is_trade_item", "i_index", "lootList", "Stack", "charge",
        "HasOwner", "Fresh", "Timestamp", "is_execute", "Stolen_Days", "Stolen_Days_Timestamp", "Town", "atrdlist",
    ];

    private static ItemCatalog Catalog() =>
        new(GameFiles.Tables, GameFiles.Localization, GameFiles.Sprites, GameFiles.Objects);

    private static GmObject LoadSave(string file) =>
        (GmObject)GmJson.Parse(SaveCodec.Decode(File.ReadAllBytes(file)).Json);

    private static IEnumerable<string> AllSaves() =>
        Directory.EnumerateFiles(Fixtures.RealSavesRoot, "data.sav", SearchOption.AllDirectories);

    /// <summary>Every <c>[name, props, …]</c> item entry anywhere in a save: inventory, stash, merchants, containers.</summary>
    private static IEnumerable<(string Name, GmObject Props)> Instances(GmNode node)
    {
        switch (node)
        {
            case GmArray a:
                if (a.Items.Count >= 2 && a.Items[0] is GmString s && a.Items[1] is GmObject p)
                    yield return (s.Value, p);
                foreach (var child in a.Items)
                foreach (var x in Instances(child))
                    yield return x;
                break;
            case GmObject o:
                foreach (var (_, child) in o.Entries)
                foreach (var x in Instances(child))
                    yield return x;
                break;
        }
    }

    /// <summary>Keys whose values differ, ignoring per-instance keys and any random enchantment on the real item.</summary>
    private static List<string> Differences(GmObject real, GmObject built)
    {
        var ignore = new HashSet<string>(InstanceKeys);
        var enchantments = real.Entries.Where(e => e.Key.StartsWith("Char", StringComparison.Ordinal)).ToList();
        foreach (var (k, v) in enchantments)
        {
            ignore.Add(k);
            ignore.Add(Regex.Match(((GmString)v).Value, @"^\w+").Value); // "Weapon_Damage +4%" → Weapon_Damage
        }
        var quality = real["quality"] is GmNumber q ? q.Value : 1;
        if (enchantments.Count > 0 || (quality != 1 && quality != 6))
            ignore.UnionWith(["quality", "Suffix"]);

        var keys = real.Entries.Select(e => e.Key).Union(built.Entries.Select(e => e.Key)).Where(k => !ignore.Contains(k));
        return keys.Where(k => Text(real[k]) != Text(built[k])).Select(k => $"{k}: {Text(real[k])} vs built {Text(built[k])}").ToList();
    }

    private static string Text(GmNode? node) => node switch
    {
        null => "(missing)",
        GmNumber n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        GmString s => '"' + s.Value + '"',
        _ => Encoding.UTF8.GetString(GmJson.Write(node)),
    };

    private static GmObject RealProps(string name) =>
        Instances(LoadSave(Path.Combine(Fixtures.RealSavesRoot, "character_2", "exitsave_1", "data.sav"))).First(i => i.Name == name).Props;

    [Theory]
    [InlineData("Guard Crossbow", true)]
    [InlineData("Voulge", true)]
    [InlineData("Road Guard Brigandine", false)]
    [InlineData("Flat Kettle Helmet", false)]
    [InlineData("Splinted Boots", false)]
    [InlineData("Splint Vambraces", false)]
    [InlineData("Chain Belt", false)]
    [InlineData("Azure Cape", false)]
    [InlineData("Antique Copper Ring", false)]
    [InlineData("Fang Pendant", false)]
    public void Gear_props_match_real_items(string name, bool isWeapon)
    {
        var tables = GameFiles.Tables;
        var row = (isWeapon ? tables.Weapons : tables.Armor).Find(name)!;
        Assert.Empty(Differences(RealProps(name), ItemPropsBuilder.FromGearRow(row, isWeapon)!));
    }

    [Fact]
    public void Gear_props_match_every_gear_instance_in_every_save()
    {
        var tables = GameFiles.Tables;
        var checkedCount = 0;
        var failures = new List<string>();
        foreach (var save in AllSaves())
        foreach (var (name, props) in Instances(LoadSave(save)).Where(i => i.Props["Metatype"] is not null))
        {
            var weapon = tables.Weapons.Find(name);
            var row = weapon ?? tables.Armor.Find(name);
            if (row is null)
                continue;
            checkedCount++;
            failures.AddRange(Differences(props, ItemPropsBuilder.FromGearRow(row, weapon is not null)!).Select(d => $"{name}: {d}"));
        }
        Assert.Empty(failures);
        Assert.True(checkedCount >= 200, $"only {checkedCount} gear instances checked");
    }

    [Theory]
    [InlineData("o_inv_salve")]
    [InlineData("o_inv_bandage")]
    [InlineData("o_inv_wineskin")]
    [InlineData("o_inv_cheese")]
    [InlineData("o_inv_mead")]
    [InlineData("o_inv_moneybag")]
    public void Consumable_props_match_real_items(string obj)
    {
        var row = GameFiles.Tables.ItemsStats.Find(obj["o_inv_".Length..])!;
        Assert.Empty(Differences(RealProps(obj), ItemPropsBuilder.FromItemsStatsRow(row)!));
    }

    [Theory]
    [InlineData("torch")]
    [InlineData("tinker")]
    [InlineData("beartrap")]
    [InlineData("bodkin_arrows")]
    public void Tools_and_ammo_are_not_built_from_tables(string id) =>
        Assert.Null(ItemPropsBuilder.FromItemsStatsRow(GameFiles.Tables.ItemsStats.Find(id)!));

    [Fact]
    public void Item_props_match_every_buildable_instance_in_every_save()
    {
        var items = GameFiles.Tables.ItemsStats;
        var checkedCount = 0;
        var failures = new List<string>();
        foreach (var save in AllSaves())
        foreach (var (name, props) in Instances(LoadSave(save)))
        {
            if (!name.StartsWith("o_inv_", StringComparison.Ordinal) || props["Metatype"] is not null)
                continue;
            var row = items.Find(name["o_inv_".Length..]);
            var built = row is null ? null : ItemPropsBuilder.FromItemsStatsRow(row);
            if (built is null)
                continue;
            checkedCount++;
            failures.AddRange(Differences(props, built).Select(d => $"{name}: {d}"));
        }
        Assert.Empty(failures);
        Assert.True(checkedCount >= 1000, $"only {checkedCount} item instances checked");
    }

    [Fact]
    public void Catalog_lists_game_items_with_availability()
    {
        var root = LoadSave(Path.Combine(Fixtures.RealSavesRoot, "character_2", "exitsave_1", "data.sav"));
        var all = Catalog().All(root);

        Assert.True(all.Count > 1000, $"only {all.Count} catalog items");
        var treatise = all.Single(i => i.Key == "o_inv_treatise_pyro1");
        Assert.Equal(CatalogAvailability.Ready, treatise.Availability);
        Assert.Equal("Pyromancy Treatise I", treatise.DisplayName);
        Assert.Equal("book", treatise.Category);
        Assert.Equal(CatalogAvailability.CloneFromSave, all.Single(i => i.Key == "o_inv_torch").Availability);
        var crossbow = all.Single(i => i.Key == "Guard Crossbow");
        Assert.Equal((CatalogAvailability.Ready, "crossbow", "s_inv_guardcrossbow", 2, 3),
            (crossbow.Availability, crossbow.Category, crossbow.Sprite, crossbow.W, crossbow.H));
        Assert.Equal("Road Guard Armor", all.Single(i => i.Key == "Road Guard Brigandine").DisplayName);
        Assert.Contains(all, i => i.Availability == CatalogAvailability.Unavailable);
        Assert.All(all.Where(i => i.Availability == CatalogAvailability.Unavailable), i => Assert.False(string.IsNullOrEmpty(i.Reason)));
        Assert.Equal(all.Count, all.Select(i => i.Key).Distinct().Count());
    }

    [Fact]
    public void Added_consumable_lands_in_bag_with_observed_charges()
    {
        var session = Sessions.OpenVelmir();
        var inv = new InventoryEditor(session, new GameItemShapes(GameFiles.Sprites, GameFiles.Objects));

        var index = inv.Add(Catalog().CreateEntry("o_inv_salve", session.Root));

        var item = inv.Read()[index];
        Assert.Equal(("o_inv_salve", 7, 0, 3.0), (item.Name, item.X!.Value, item.Y!.Value, item.Charges!.Value));
    }

    [Fact]
    public void Added_gear_is_new_and_fully_repaired()
    {
        var session = Sessions.OpenVelmir();
        var inv = new InventoryEditor(session, new GameItemShapes(GameFiles.Sprites, GameFiles.Objects));

        var index = inv.Add(Catalog().CreateEntry("Guard Crossbow", session.Root));

        var item = inv.Read()[index];
        Assert.Equal(("Guard Crossbow", 7, 0, 2, 3), (item.Name, item.X!.Value, item.Y!.Value, item.W, item.H));
        Assert.Equal(40, item.Durability);
        Assert.Equal(40, item.MaxDurability);
        Assert.True(item.IsGear);
    }

    [Fact]
    public void Cloned_items_drop_ownership_markers()
    {
        var root = new GmObject();
        var stolen = (GmObject)GmJson.Parse("{ \"idName\": \"torch\", \"Duration\": 10.0, \"MaxDuration\": 50.0, \"HasOwner\": 1.0, \"Stolen_Days\": 3.0, \"Stolen_Days_Timestamp\": 5.0, \"Town\": \"Osbrook\" }"u8);
        var list = new GmArray();
        var entry = new GmArray();
        entry.Items.AddRange([GmString.From("o_inv_torch"), stolen]);
        list.Items.Add(entry);
        root.Set("inventoryDataList", list);

        var clone = ItemPropsBuilder.CloneFromSave("o_inv_torch", root)!;

        Assert.Equal("{ \"idName\": \"torch\", \"Duration\": 50.0, \"MaxDuration\": 50.0 }", Encoding.UTF8.GetString(GmJson.Write(clone)));
        Assert.Null(ItemPropsBuilder.CloneFromSave("o_inv_nothing", root));
    }

    [Fact]
    public void Unavailable_items_cannot_be_created()
    {
        var root = LoadSave(Path.Combine(Fixtures.RealSavesRoot, "character_2", "exitsave_1", "data.sav"));
        var unavailable = Catalog().All(root).First(i => i.Availability == CatalogAvailability.Unavailable);
        Assert.Throws<EditRejectedException>(() => Catalog().CreateEntry(unavailable.Key, root));
        Assert.Throws<EditRejectedException>(() => Catalog().CreateEntry("not an item", root));
    }

    [Fact]
    public void Shapes_resolve_gear_and_objects()
    {
        var shapes = new GameItemShapes(GameFiles.Sprites, GameFiles.Objects);
        Assert.Equal("s_inv_guardcrossbow", shapes.SpriteFor("Guard Crossbow"));
        Assert.Equal((2, 3), shapes.Footprint("Road Guard Brigandine"));
        Assert.Equal("s_inv_bondage", shapes.SpriteFor("o_inv_bandage"));
        Assert.Equal((1, 3), shapes.Footprint("o_inv_torch"));
        Assert.Equal(("missing", (1, 1)), (shapes.SpriteFor("No Such Thing"), shapes.Footprint("No Such Thing")));
    }
}

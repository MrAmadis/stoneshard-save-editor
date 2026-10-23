using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Tests.Editing;

public class InventoryEditorTests
{
    /// <summary>Velmir's real footprints (icon size / 27 px), so tests don't need the game install.</summary>
    private sealed class FakeShapes : IItemShapeProvider
    {
        private static readonly Dictionary<string, (int, int)> Sizes = new()
        {
            ["o_inv_torch"] = (1, 3), ["o_inv_tinker"] = (1, 3),
            ["o_inv_wineskin"] = (1, 2), ["o_inv_moneybag"] = (1, 2), ["o_inv_mead"] = (1, 2),
            ["o_inv_potion04_empty"] = (1, 2), ["o_inv_brandy"] = (1, 2), ["o_inv_aqua_vitae"] = (1, 2),
            ["o_inv_splint"] = (2, 1), ["Flat Kettle Helmet"] = (2, 2),
        };

        private static readonly Dictionary<string, string> Kinds = new()
        {
            ["Flat Kettle Helmet"] = "Head", ["Road Guard Brigandine"] = "Chest", ["Splint Vambraces"] = "Arms",
            ["Splinted Boots"] = "Legs", ["Chain Belt"] = "Waist", ["Fang Pendant"] = "Amulet", ["Azure Cape"] = "Back",
            ["Velmir Ring"] = "Ring", ["Antique Copper Ring"] = "Ring", ["Voulge"] = "2haxe", ["Guard Crossbow"] = "crossbow",
            ["o_inv_leafshaped_bolts"] = "ammo",
            // Test-only gear, 1×1.
            ["Peasant Hat"] = "Head", ["Board Shield"] = "shield",
        };

        public (int W, int H) Footprint(string itemName) => Sizes.GetValueOrDefault(itemName, (1, 1));

        public string SpriteFor(string itemName) => "s_" + itemName;

        public int MaxStack(string itemName) => itemName switch
        {
            "o_inv_leafshaped_bolts" => 20,
            "o_inv_gold" => 100,
            _ => 0,
        };

        public string Kind(string itemName) => Kinds.GetValueOrDefault(itemName, "");
    }

    private static (SaveSession Session, InventoryEditor Inv) Open()
    {
        var session = Sessions.OpenVelmir();
        return (session, new InventoryEditor(session, new FakeShapes()));
    }

    private static GmArray Entry(SaveSession s, int index) =>
        (GmArray)((GmArray)s.Root["inventoryDataList"]!).Items[index];

    private static ItemView Item(InventoryEditor inv, string name, int? x = null, int? y = null) =>
        inv.Read().First(i => i.Name == name && (x is null || i.X == x) && (y is null || i.Y == y));

    [Fact]
    public void Read_lists_33_items_12_equipped()
    {
        var (_, inv) = Open();
        var items = inv.Read();
        Assert.Equal(33, items.Count);
        Assert.Equal(12, items.Count(i => i.Equipped));
        Assert.Equal(21, items.Count(i => !i.Equipped));
        Assert.All(items.Where(i => i.Equipped), i => Assert.Null(i.X));
        Assert.Equal("o_inv_right_hand", items.Single(i => i.Name == "Voulge").Slot);
    }

    [Fact]
    public void Bag_positions_come_from_cell()
    {
        var (_, inv) = Open();
        var moneybag = Item(inv, "o_inv_moneybag");
        Assert.Equal((3, 0), (moneybag.X, moneybag.Y));
        Assert.Equal(0, moneybag.Stack); // the coins inside; its cached 2864 is stale (session B)
        Assert.Null(moneybag.Charges);
        Assert.Equal("s_o_inv_moneybag", moneybag.Sprite);
        var splint = Item(inv, "o_inv_splint");
        Assert.Equal((2, 4, 2, 1), (splint.X, splint.Y, splint.W, splint.H));
    }

    [Fact]
    public void Item_kinds_expose_the_right_values()
    {
        var (_, inv) = Open();
        var salve = Item(inv, "o_inv_salve", 5, 1);
        Assert.Equal(3, salve.Charges);
        Assert.False(salve.IsGear);
        Assert.Null(salve.MaxDurability);

        var helmet = Item(inv, "Flat Kettle Helmet");
        Assert.True(helmet.IsGear);
        Assert.Null(helmet.Charges);
        Assert.Equal(105, helmet.Durability);
        Assert.Equal(105, helmet.MaxDurability);

        var tools = Item(inv, "o_inv_tinker");
        Assert.Equal(37, tools.Durability);
        Assert.Equal(75, tools.MaxDurability);
    }

    /// <summary>A gold coin entry as the catalog makes it: a full stack of 100.</summary>
    private static GmArray NewCoin() => (GmArray)GmJson.Parse(
        "[\"o_inv_gold\", { \"Material\": \"gold\", \"idName\": \"gold\", \"Duration\": 0.0, \"MaxDuration\": 0.0, \"i_index\": 0.0, \"charge\": 1.0 }, 0.0, -4.0, 0.0, 1.0, 100.0, 0.0, 0.0, \"N/A\"]"u8);

    private static GmArray Purse(SaveSession s, InventoryEditor inv) =>
        Entry(s, inv.Read().First(i => i.Name == "o_inv_moneybag").Index);

    private static List<(string Name, int Cell, double Count)> Contents(GmArray purse) =>
        ((GmArray)((GmObject)purse.Items[1])["lootList"]!).Items.Cast<GmArray>()
        .Select(e => (((GmString)e.Items[0]).Value, (int)((GmNumber)e.Items[3]).Value, ((GmNumber)e.Items[6]).Value))
        .ToList();

    private static double Cached(GmArray purse) => ((GmNumber)((GmObject)purse.Items[1])["Stack"]!).Value;

    [Fact]
    public void Gold_counts_real_coins_not_the_purse_total()
    {
        // Velmir's purse says 2864 but holds no coins; the game drops it to 0 when the purse is opened (session B).
        var (_, inv) = Open();
        var purse = Item(inv, "o_inv_moneybag");

        Assert.Equal(0, inv.Gold());
        Assert.Equal(0, purse.Stack);
        Assert.Equal(new[] { (purse.Index, 2864, 0) }, inv.StalePurses());
    }

    [Fact]
    public void SetGold_fills_the_purse_with_coin_stacks()
    {
        var (session, inv) = Open();

        inv.SetGold(1234, () => throw new InvalidOperationException("the purse has room"), NewCoin);

        var coins = Contents(Purse(session, inv));
        Assert.Equal(13, coins.Count);
        Assert.Equal(1234, coins.Sum(c => c.Count));
        Assert.All(coins, c => Assert.Equal("o_inv_gold", c.Name));
        Assert.Equal(new[] { 0, 5, 10, 15, 1 }, coins.Take(5).Select(c => c.Cell)); // down the first column, as the game does
        Assert.Equal(coins.Count, coins.Select(c => c.Cell).Distinct().Count());
        Assert.All(coins, c => Assert.InRange(c.Cell, 0, InventoryEditor.PurseSlots - 1));
        Assert.Equal(1234, Cached(Purse(session, inv)));
        Assert.Equal(1234, inv.Gold());
        Assert.Empty(inv.StalePurses());
    }

    [Fact]
    public void SetGold_adds_purses_when_one_is_full()
    {
        var (session, inv) = Open();
        var template = (GmArray)Purse(session, inv).DeepClone();

        inv.SetGold(4500, () => (GmArray)template.DeepClone(), NewCoin);

        var purses = inv.Read().Where(i => i.Name == "o_inv_moneybag").ToList();
        Assert.Equal(new[] { 2000.0, 2000, 500 }, purses.Select(p => p.Stack!.Value));
        Assert.Equal(4500, inv.Gold());
        Assert.All(purses, p => Assert.NotNull(p.X));
    }

    [Fact]
    public void SetGold_keeps_loose_coins_and_valuables()
    {
        var (session, inv) = Open();
        inv.Add(NewCoin());
        var loot = (GmArray)((GmObject)Purse(session, inv).Items[1])["lootList"]!;
        var gem = NewCoin();
        gem.Items[0] = GmString.From("o_inv_ruby");
        gem.Items[3] = GmNumber.From(0);
        loot.Items.Add(gem);

        inv.SetGold(1000, () => throw new InvalidOperationException(), NewCoin);

        var contents = Contents(Purse(session, inv));
        Assert.Contains(("o_inv_ruby", 0, 100.0), contents);
        Assert.Equal(900, contents.Where(c => c.Name == "o_inv_gold").Sum(c => c.Count));
        Assert.DoesNotContain(contents, c => c.Name == "o_inv_gold" && c.Cell == 0);
        Assert.Equal(900, Cached(Purse(session, inv)));
        Assert.Equal(1000, inv.Gold());
        Assert.Throws<EditRejectedException>(() => inv.SetGold(50, () => throw new InvalidOperationException(), NewCoin));
    }

    [Fact]
    public void SetGold_without_room_for_purses_changes_nothing()
    {
        var (session, inv) = Open();
        var template = (GmArray)Purse(session, inv).DeepClone();
        var before = session.ChangeCount;

        var error = Assert.Throws<EditRejectedException>(() => inv.SetGold(99_999, () => (GmArray)template.DeepClone(), NewCoin));

        Assert.Contains("purse", error.Message);
        Assert.Equal(before, session.ChangeCount);
        Assert.Empty(Contents(Purse(session, inv)));
    }

    [Fact]
    public void SetPurseGold_caps_at_twenty_stacks()
    {
        var (session, inv) = Open();
        var index = Item(inv, "o_inv_moneybag").Index;

        Assert.Throws<EditRejectedException>(() => inv.SetPurseGold(index, 2001, NewCoin));
        inv.SetPurseGold(index, 2000, NewCoin);

        Assert.Equal(20, Contents(Purse(session, inv)).Count);
        Assert.Equal(2000, Item(inv, "o_inv_moneybag").Stack);
        Assert.Throws<EditRejectedException>(() => inv.SetPurseGold(Item(inv, "o_inv_cheese").Index, 10, NewCoin));
    }

    [Fact]
    public void Charges_are_capped_at_the_most_seen_for_the_item()
    {
        // The game keeps the real maximum in code; full copies in traders' stock and loot show it (salve 3, owner).
        var (_, inv) = Open();
        var salve = Item(inv, "o_inv_salve", 5, 1);
        Assert.Equal(3, salve.MaxCharges);
        Assert.Equal(6, Item(inv, "o_inv_wineskin").MaxCharges);
        Assert.Null(Item(inv, "Flat Kettle Helmet").MaxCharges);

        var error = Assert.Throws<EditRejectedException>(() => inv.SetCharges(salve.Index, 4));
        Assert.Equal("Charges must be a whole number between 1 and 3", error.Message);
        inv.SetCharges(salve.Index, 3);
    }

    [Fact]
    public void SetCharges_updates_props_and_mirror()
    {
        var (session, inv) = Open();
        var salve = Item(inv, "o_inv_salve", 5, 1);

        inv.SetCharges(salve.Index, 1);

        var entry = Entry(session, salve.Index);
        Assert.Equal("1.0", ((GmNumber)((GmObject)entry.Items[1])["charge"]!).Raw);
        Assert.Equal("1.0", ((GmNumber)entry.Items[5]).Raw);
        Assert.Equal(1, session.ChangeCount);
    }

    [Fact]
    public void SetDurability_on_items_with_durability_only()
    {
        var (session, inv) = Open();
        var helmet = Item(inv, "Flat Kettle Helmet");

        inv.SetDurability(helmet.Index, 50, 105);
        Assert.Equal("50.0", ((GmNumber)((GmObject)Entry(session, helmet.Index).Items[1])["Duration"]!).Raw);

        Assert.Throws<EditRejectedException>(() => inv.SetDurability(helmet.Index, 106, 105));
        Assert.Throws<EditRejectedException>(() => inv.SetDurability(Item(inv, "o_inv_salve", 5, 1).Index, 1, 1));
    }

    [Fact]
    public void SetCount_updates_stack_slot()
    {
        var (session, inv) = Open();
        var bolts = Item(inv, "o_inv_leafshaped_bolts");
        Assert.Equal(19, bolts.Count);
        Assert.Null(bolts.Charges);

        inv.SetCount(bolts.Index, 5);

        Assert.Equal(5, Item(inv, "o_inv_leafshaped_bolts").Count);
        Assert.Equal(5, ((GmNumber)Entry(session, bolts.Index).Items[6]).Value);
        Assert.Throws<EditRejectedException>(() => inv.SetCount(bolts.Index, 21));
        Assert.Throws<EditRejectedException>(() => inv.SetCount(bolts.Index, 0));
        var cheese = Item(inv, "o_inv_cheese");
        Assert.Null(cheese.Count);
        Assert.Throws<EditRejectedException>(() => inv.SetCount(cheese.Index, 2));
    }

    [Fact]
    public void Duplicating_a_stack_keeps_its_count()
    {
        var (_, inv) = Open();

        var index = inv.Duplicate(Item(inv, "o_inv_leafshaped_bolts").Index);

        var copy = inv.Read()[index];

        Assert.False(copy.Equipped);
        Assert.Equal(19, copy.Count);
    }

    [Fact]
    public void FirstFit_skips_occupied_cells()
    {
        var (_, inv) = Open();
        Assert.Equal((7, 0), inv.FirstFit(1, 1));
        Assert.Equal((7, 0), inv.FirstFit(2, 2));
        Assert.Equal((7, 0), inv.FirstFit(1, 3));
        Assert.Null(inv.FirstFit(11, 1));
    }

    [Fact]
    public void Duplicate_places_copy_in_first_gap()
    {
        var (session, inv) = Open();
        var salve = Item(inv, "o_inv_salve", 5, 1);

        var copy = inv.Duplicate(salve.Index);

        var view = inv.Read()[copy];
        Assert.Equal(("o_inv_salve", 7, 0, 3.0), (view.Name, view.X!.Value, view.Y!.Value, view.Charges!.Value));
        Assert.Equal((5, 1), (Item(inv, "o_inv_salve", 5, 1).X!.Value, Item(inv, "o_inv_salve", 5, 1).Y!.Value));
        Assert.Equal(34, inv.Read().Count);
        Assert.Equal(1, session.ChangeCount);
    }

    [Fact]
    public void Duplicating_equipped_gear_puts_a_copy_in_the_bag()
    {
        var (session, inv) = Open();
        var copy = inv.Duplicate(Item(inv, "Fang Pendant").Index);

        var entry = Entry(session, copy);
        Assert.Equal("N/A", ((GmString)entry.Items[9]).Value);
        Assert.Equal("7.0", ((GmNumber)entry.Items[3]).Raw);
        var pendants = inv.Read().Where(i => i.Name == "Fang Pendant").ToList();
        Assert.Equal(2, pendants.Count);
        Assert.Single(pendants, p => p.Equipped);
        Assert.Single(pendants, p => !p.Equipped && p.X == 7 && p.Y == 0);
        Assert.Equal(1, session.ChangeCount);
    }

    [Fact]
    public void Bag_full_rejects_duplicate_and_add()
    {
        var (session, inv) = Open();
        var salve = Item(inv, "o_inv_salve", 5, 1).Index;
        for (var i = 0; i < 17; i++) // Velmir has 17 free cells in a 10x5 bag
            inv.Duplicate(salve);
        var count = inv.Read().Count;

        var e = Assert.Throws<EditRejectedException>(() => inv.Duplicate(salve));
        Assert.Equal("No room in the bag", e.Message);
        Assert.Throws<EditRejectedException>(() => inv.Add((GmArray)Entry(session, salve).DeepClone()));
        Assert.Equal(count, inv.Read().Count);
    }

    [Fact]
    public void Add_places_entry_at_first_fit()
    {
        var (session, inv) = Open();
        var entry = (GmArray)Entry(session, Item(inv, "o_inv_cheese").Index).DeepClone();

        var index = inv.Add(entry);

        Assert.Equal((7, 0), (inv.Read()[index].X!.Value, inv.Read()[index].Y!.Value));
    }

    [Fact]
    public void Delete_removes_entry()
    {
        var (_, inv) = Open();
        inv.Delete(Item(inv, "o_inv_cheese").Index);
        Assert.Equal(32, inv.Read().Count);
        Assert.DoesNotContain(inv.Read(), i => i.Name == "o_inv_cheese");
    }

    [Fact]
    public void SetGold_creates_a_purse_and_zero_empties_it()
    {
        var (session, inv) = Open();
        var template = (GmArray)Purse(session, inv).DeepClone();
        inv.Delete(Item(inv, "o_inv_moneybag").Index);

        inv.SetGold(50, () => (GmArray)template.DeepClone(), NewCoin);
        var created = Item(inv, "o_inv_moneybag");
        Assert.Equal((3, 0, 50.0), (created.X!.Value, created.Y!.Value, created.Stack!.Value));
        Assert.Equal(50, inv.Gold());

        inv.SetGold(0, () => throw new InvalidOperationException(), NewCoin);
        Assert.Empty(Contents(Purse(session, inv)));
        Assert.Equal(0, inv.Gold());
    }

    [Fact]
    public void Inventory_rejects_invalid_numbers()
    {
        var (session, inv) = Open();
        var salve = Item(inv, "o_inv_salve", 5, 1).Index;
        var helmet = Item(inv, "Flat Kettle Helmet").Index;

        Assert.Throws<EditRejectedException>(() => inv.SetCharges(salve, 0));
        Assert.Throws<EditRejectedException>(() => inv.SetCharges(salve, -1));
        Assert.Throws<EditRejectedException>(() => inv.SetCharges(salve, 100));
        Assert.Throws<EditRejectedException>(() => inv.SetGold(-5, () => throw new InvalidOperationException(), NewCoin));
        Assert.Throws<EditRejectedException>(() => inv.SetDurability(helmet, double.NaN, 105));
        Assert.Throws<EditRejectedException>(() => inv.SetDurability(helmet, 10, 10.5));
        Assert.Throws<EditRejectedException>(() => inv.Delete(999));
        Assert.Equal(0, session.ChangeCount);
    }
}

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
            ["o_inv_splint"] = (2, 1),
        };

        public (int W, int H) Footprint(string itemName) => Sizes.GetValueOrDefault(itemName, (1, 1));

        public string SpriteFor(string itemName) => "s_" + itemName;
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
        Assert.Equal(2864, moneybag.Stack);
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

    [Fact]
    public void Gold_reads_2864() => Assert.Equal(2864, Open().Inv.Gold());

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
    public void SetStack_needs_a_stack()
    {
        var (_, inv) = Open();
        inv.SetStack(Item(inv, "o_inv_moneybag").Index, 10);
        Assert.Equal(10, Item(inv, "o_inv_moneybag").Stack);
        Assert.Throws<EditRejectedException>(() => inv.SetStack(Item(inv, "o_inv_cheese").Index, 3));
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
    public void SetGold_updates_creates_and_deletes()
    {
        var (session, inv) = Open();
        var moneybagTemplate = (GmArray)Entry(session, Item(inv, "o_inv_moneybag").Index).DeepClone();

        inv.SetGold(99999, () => throw new InvalidOperationException("should reuse the existing bag"));
        Assert.Equal("99999.0", ((GmNumber)((GmObject)Entry(session, Item(inv, "o_inv_moneybag").Index).Items[1])["Stack"]!).Raw);

        inv.Delete(Item(inv, "o_inv_moneybag").Index);
        inv.SetGold(50, () => (GmArray)moneybagTemplate.DeepClone());
        var created = Item(inv, "o_inv_moneybag");
        Assert.Equal((3, 0, 50.0), (created.X!.Value, created.Y!.Value, created.Stack!.Value));
        Assert.Equal(50, inv.Gold());

        inv.SetGold(0, () => throw new InvalidOperationException());
        Assert.DoesNotContain(inv.Read(), i => i.Name == "o_inv_moneybag");
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
        Assert.Throws<EditRejectedException>(() => inv.SetGold(-5, () => throw new InvalidOperationException()));
        Assert.Throws<EditRejectedException>(() => inv.SetDurability(helmet, double.NaN, 105));
        Assert.Throws<EditRejectedException>(() => inv.SetDurability(helmet, 10, 10.5));
        Assert.Throws<EditRejectedException>(() => inv.Delete(999));
        Assert.Equal(0, session.ChangeCount);
    }
}

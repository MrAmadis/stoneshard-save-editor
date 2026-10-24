using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Tests.Editing;

/// <summary>
/// Moving items (spec §5). Velmir wears a Voulge (two-handed) in set 0 and a crossbow with its bolts in set 1; set 0 is
/// the active one. Entries are <c>[name, props, f2, cell, f4, chargesMirror, count, f7, weaponSet, slot]</c>.
/// </summary>
public class InventoryMoveTests
{
    private const int F2 = 2, CellIndex = 3, CountIndex = 6, F7 = 7, WeaponSet = 8, SlotIndex = 9;

    private static (SaveSession Session, InventoryEditor Inv) Open()
    {
        var session = Sessions.OpenVelmir();
        return (session, new InventoryEditor(session, new FakeShapes()));
    }

    private static GmArray Entry(SaveSession s, int index) => (GmArray)((GmArray)s.Root["inventoryDataList"]!).Items[index];

    private static ItemView Item(InventoryEditor inv, string name) => inv.Read().First(i => i.Name == name);

    private static int Index(InventoryEditor inv, string name) => Item(inv, name).Index;

    private static double Number(GmArray entry, int field) => ((GmNumber)entry.Items[field]).Value;

    private static string Slot(GmArray entry) => ((GmString)entry.Items[SlotIndex]).Value;

    private static void EmptyBag(InventoryEditor inv)
    {
        foreach (var index in inv.Read().Where(i => !i.Equipped).Select(i => i.Index).OrderDescending())
            inv.Delete(index);
    }

    /// <summary>A test-only 1×1 piece of gear (see <see cref="FakeShapes"/>) in the bag at (x, y).</summary>
    private static int TestGear(SaveSession s, InventoryEditor inv, string name, int x, int y)
    {
        var copy = inv.Duplicate(Index(inv, "Fang Pendant"));
        Entry(s, copy).Items[0] = GmString.From(name);
        inv.Move(copy, ItemPlace.InBag(x, y));
        return copy;
    }

    [Fact]
    public void Moving_in_the_bag_changes_only_the_cell()
    {
        var (s, inv) = Open();
        var torch = Index(inv, "o_inv_torch");
        var spot = inv.FirstFit(1, 3)!.Value;
        var expected = (GmArray)Entry(s, torch).DeepClone();
        expected.Items[CellIndex] = GmNumber.From(spot.Y * InventoryEditor.Columns + spot.X);

        inv.Move(torch, ItemPlace.InBag(spot.X, spot.Y));

        Assert.Equal(GmJson.Write(expected), GmJson.Write(Entry(s, torch)));
        Assert.Equal((spot.X, spot.Y), (Item(inv, "o_inv_torch").X!.Value, Item(inv, "o_inv_torch").Y!.Value));
    }

    [Fact]
    public void An_item_can_shift_onto_cells_it_is_leaving()
    {
        var (_, inv) = Open();
        var torch = Index(inv, "o_inv_torch");
        var (x, y) = inv.FirstFit(1, 4)!.Value;
        inv.Move(torch, ItemPlace.InBag(x, y));

        inv.Move(torch, ItemPlace.InBag(x, y + 1));

        Assert.Equal((x, y + 1), (Item(inv, "o_inv_torch").X!.Value, Item(inv, "o_inv_torch").Y!.Value));
    }

    [Fact]
    public void Moving_onto_a_taken_spot_is_refused_and_changes_nothing()
    {
        var (s, inv) = Open();
        var torch = Index(inv, "o_inv_torch");
        var purse = Item(inv, "o_inv_moneybag");
        var to = ItemPlace.InBag(purse.X!.Value, purse.Y!.Value);

        Assert.Equal("There's no room there", inv.Refusal(torch, to));
        Assert.Throws<EditRejectedException>(() => inv.Move(torch, to));
        Assert.Equal(0, s.ChangeCount);
    }

    [Fact]
    public void Moving_past_the_edge_is_refused()
    {
        var (_, inv) = Open();
        var torch = Index(inv, "o_inv_torch");

        Assert.Equal("There's no room there", inv.Refusal(torch, ItemPlace.InBag(0, InventoryEditor.BagRows - 1)));
        Assert.Equal("There's no room there", inv.Refusal(torch, ItemPlace.InBag(InventoryEditor.Columns, 0)));
    }

    [Fact]
    public void Unequipping_writes_the_bag_form()
    {
        var (s, inv) = Open();
        var helmet = Index(inv, "Flat Kettle Helmet");
        var props = GmJson.Write(Entry(s, helmet).Items[1]);
        var spot = inv.FirstFit(2, 2)!.Value;

        inv.Move(helmet, ItemPlace.InBag(spot.X, spot.Y));

        var entry = Entry(s, helmet);
        Assert.Equal(0, Number(entry, F2));
        Assert.Equal(spot.Y * InventoryEditor.Columns + spot.X, Number(entry, CellIndex));
        Assert.Equal(0, Number(entry, F7));
        Assert.Equal(0, Number(entry, WeaponSet));
        Assert.Equal("N/A", Slot(entry));
        Assert.Equal(props, GmJson.Write(entry.Items[1]));
    }

    [Fact]
    public void Equipping_writes_the_worn_form()
    {
        var (s, inv) = Open();
        var helmet = Index(inv, "Flat Kettle Helmet");
        var spot = inv.FirstFit(2, 2)!.Value;
        inv.Move(helmet, ItemPlace.InBag(spot.X, spot.Y));

        inv.Move(helmet, ItemPlace.Worn(GearRules.Head));

        var entry = Entry(s, helmet);
        Assert.Equal(-4, Number(entry, F2));
        Assert.Equal(-4, Number(entry, CellIndex));
        Assert.True(entry.Items[F7] is GmBool { Value: true });
        Assert.Equal(0, Number(entry, WeaponSet));
        Assert.Equal(GearRules.Head, Slot(entry));
    }

    [Fact]
    public void Swapping_puts_the_old_item_where_the_new_one_was()
    {
        var (s, inv) = Open();
        var original = Entry(s, Index(inv, "Flat Kettle Helmet"));
        var copy = inv.Duplicate(Index(inv, "Flat Kettle Helmet"));
        var spot = inv.Read()[copy];

        var result = inv.Move(copy, ItemPlace.Worn(GearRules.Head));

        Assert.Null(result.Removed);
        Assert.Equal(GearRules.Head, Slot(Entry(s, copy)));
        Assert.Equal("N/A", Slot(original));
        Assert.Equal(spot.Y!.Value * InventoryEditor.Columns + spot.X!.Value, Number(original, CellIndex));
    }

    [Fact]
    public void A_swapped_out_item_that_doesnt_fit_the_freed_spot_goes_to_the_first_free_one()
    {
        var (s, inv) = Open();
        EmptyBag(inv);
        var hat = TestGear(s, inv, "Peasant Hat", 9, 4);

        inv.Move(hat, ItemPlace.Worn(GearRules.Head));

        var helmet = Item(inv, "Flat Kettle Helmet");
        Assert.False(helmet.Equipped);
        Assert.Equal((0, 0), (helmet.X!.Value, helmet.Y!.Value));
    }

    [Fact]
    public void With_no_room_the_swapped_out_item_is_removed()
    {
        var (s, inv) = Open();
        EmptyBag(inv);
        var hat = TestGear(s, inv, "Peasant Hat", 9, 4);
        for (var i = 0; i < InventoryEditor.Columns * InventoryEditor.BagRows - 1; i++)
            inv.Duplicate(Index(inv, "Fang Pendant"));

        var result = inv.Move(hat, ItemPlace.Worn(GearRules.Head));

        Assert.Equal("Flat Kettle Helmet", result.Removed);
        Assert.DoesNotContain(inv.Read(), i => i.Name == "Flat Kettle Helmet");
        Assert.Equal("Peasant Hat", inv.Read()[result.Index].Name);
        Assert.True(inv.Read()[result.Index].Equipped);
    }

    [Fact]
    public void Worn_items_trade_places_when_each_fits_the_other()
    {
        var (_, inv) = Open();

        inv.Move(Index(inv, "Velmir Ring"), ItemPlace.Worn(GearRules.Ring2));

        Assert.Equal(GearRules.Ring2, Item(inv, "Velmir Ring").Slot);
        Assert.Equal(GearRules.Ring1, Item(inv, "Antique Copper Ring").Slot);
    }

    [Fact]
    public void A_two_handed_weapon_needs_the_other_hand_free()
    {
        var (_, inv) = Open();

        Assert.Equal("A two-handed weapon needs the other hand free",
            inv.Refusal(Index(inv, "Voulge"), ItemPlace.Worn(GearRules.RightHand, 1)));
    }

    private static void ToBag(InventoryEditor inv, string name)
    {
        var item = Item(inv, name);
        var spot = inv.FirstFit(item.W, item.H)!.Value;
        inv.Move(item.Index, ItemPlace.InBag(spot.X, spot.Y));
    }

    [Fact]
    public void A_weapon_that_cant_trade_goes_to_the_bag()
    {
        var (s, inv) = Open();
        ToBag(inv, "o_inv_leafshaped_bolts");
        ToBag(inv, "Guard Crossbow");
        var spot = inv.FirstFit(1, 1)!.Value;
        inv.Move(TestGear(s, inv, "Arna Sword", spot.X, spot.Y), ItemPlace.Worn(GearRules.RightHand, 1));
        spot = inv.FirstFit(1, 1)!.Value;
        inv.Move(TestGear(s, inv, "Board Shield", spot.X, spot.Y), ItemPlace.Worn(GearRules.LeftHand, 1));

        var result = inv.Move(Index(inv, "Arna Sword"), ItemPlace.Worn(GearRules.RightHand, 0));

        Assert.Null(result.Removed);
        Assert.Equal(ItemPlace.Worn(GearRules.RightHand, 0), Item(inv, "Arna Sword").Place);
        Assert.False(Item(inv, "Voulge").Equipped); // an axe can't go beside set 2's shield
    }

    [Fact]
    public void A_two_handed_item_dropped_on_either_hand_goes_in_the_right_hand()
    {
        // Session G2: the game lets a lute, chain or pickaxe be put on either hand; it takes both.
        var (_, inv) = Open();
        ToBag(inv, "o_inv_leafshaped_bolts");
        ToBag(inv, "Guard Crossbow");

        Assert.Null(inv.Refusal(Index(inv, "Voulge"), ItemPlace.Worn(GearRules.LeftHand, 1)));
        inv.Move(Index(inv, "Voulge"), ItemPlace.Worn(GearRules.LeftHand, 1));

        Assert.Equal(ItemPlace.Worn(GearRules.RightHand, 1), Item(inv, "Voulge").Place);
    }

    [Fact]
    public void An_off_hand_in_the_active_set_is_marked_2()
    {
        // The game writes 2 for the active set's left hand: Jorgrim's off-hand axe, character_7's sling stones.
        var (s, inv) = Open();
        ToBag(inv, "o_inv_leafshaped_bolts");
        inv.Move(Index(inv, "Guard Crossbow"), ItemPlace.Worn(GearRules.RightHand, 0));

        inv.Move(Index(inv, "o_inv_leafshaped_bolts"), ItemPlace.Worn(GearRules.LeftHand, 0));

        Assert.Equal(2, Number(Entry(s, Index(inv, "o_inv_leafshaped_bolts")), F7));
    }

    [Fact]
    public void A_move_that_would_strand_ammunition_is_refused()
    {
        var (s, inv) = Open();
        var crossbow = Index(inv, "Guard Crossbow");
        var spot = inv.FirstFit(1, 1)!.Value;

        Assert.Equal("Take the ammunition off first", inv.Refusal(crossbow, ItemPlace.InBag(spot.X, spot.Y)));
        Assert.Equal("Take the ammunition off first", inv.Refusal(crossbow, ItemPlace.Worn(GearRules.RightHand, 0)));
        var sword = TestGear(s, inv, "Arna Sword", spot.X, spot.Y);
        Assert.Equal("Take the ammunition off first", inv.Refusal(sword, ItemPlace.Worn(GearRules.RightHand, 1)));
    }

    [Fact]
    public void Ammunition_goes_with_the_weapon_that_shoots_it()
    {
        var (_, inv) = Open();
        var bolts = Index(inv, "o_inv_leafshaped_bolts");

        Assert.Equal("The other hand holds a two-handed weapon", inv.Refusal(bolts, ItemPlace.Worn(GearRules.LeftHand, 0)));

        var spot = inv.FirstFit(1, 1)!.Value;
        inv.Move(bolts, ItemPlace.InBag(spot.X, spot.Y));
        inv.Move(bolts, ItemPlace.Worn(GearRules.LeftHand, 1));
        Assert.Equal(ItemPlace.Worn(GearRules.LeftHand, 1), Item(inv, "o_inv_leafshaped_bolts").Place);
    }

    [Fact]
    public void The_active_weapon_set_decides_f7()
    {
        var (s, inv) = Open();
        ToBag(inv, "o_inv_leafshaped_bolts");

        inv.Move(Index(inv, "Guard Crossbow"), ItemPlace.Worn(GearRules.RightHand, 0)); // the Voulge trades into set 2

        Assert.True(Entry(s, Index(inv, "Guard Crossbow")).Items[F7] is GmBool { Value: true });
        Assert.Equal(0, Number(Entry(s, Index(inv, "Voulge")), F7));
    }

    [Fact]
    public void Weapon_sets_saved_as_true_read_as_set_2()
    {
        var (s, inv) = Open();
        var bolts = Index(inv, "o_inv_leafshaped_bolts");
        Entry(s, bolts).Items[WeaponSet] = new GmBool(true);

        Assert.Equal(1, inv.Read()[bolts].WeaponSet);
    }

    [Fact]
    public void A_stack_keeps_its_count_and_a_purse_its_coins()
    {
        var (s, inv) = Open();
        var bolts = Index(inv, "o_inv_leafshaped_bolts");
        var spot = inv.FirstFit(1, 1)!.Value;
        inv.Move(bolts, ItemPlace.InBag(spot.X, spot.Y));
        Assert.Equal(19, Number(Entry(s, bolts), CountIndex));

        var purse = Index(inv, "o_inv_moneybag");
        var props = GmJson.Write(Entry(s, purse).Items[1]);
        var free = inv.FirstFit(1, 2)!.Value;
        inv.Move(purse, ItemPlace.InBag(free.X, free.Y));
        Assert.Equal(props, GmJson.Write(Entry(s, purse).Items[1]));
    }

    [Fact]
    public void Moving_to_where_it_already_is_changes_nothing()
    {
        var (s, inv) = Open();
        var before = GmJson.Write(s.Root["inventoryDataList"]!);

        inv.Move(Index(inv, "o_inv_torch"), Item(inv, "o_inv_torch").Place);
        inv.Move(Index(inv, "Flat Kettle Helmet"), ItemPlace.Worn(GearRules.Head));

        Assert.Equal(0, s.ChangeCount);
        Assert.Equal(before, GmJson.Write(s.Root["inventoryDataList"]!));
    }

    [Fact]
    public void A_non_hand_slot_saved_with_set_1_still_counts_as_that_slot()
    {
        var (s, inv) = Open();
        Entry(s, Index(inv, "Fang Pendant")).Items[WeaponSet] = GmNumber.From(1);

        Assert.Equal(ItemPlace.Worn(GearRules.Neck), Item(inv, "Fang Pendant").Place);
    }

    [Fact]
    public void Only_two_weapon_sets_exist()
    {
        var (_, inv) = Open();

        Assert.Equal("That doesn't go there", inv.Refusal(Index(inv, "Voulge"), ItemPlace.Worn(GearRules.RightHand, 2)));
    }

    [Fact]
    public void A_real_autosave_with_true_and_false_reads_the_right_sets()
    {
        // The game's autosaves write weaponSet and f7 as true/false (final review).
        var s = Sessions.Open("character_2", "autosave_1");
        var inv = new InventoryEditor(s, new FakeShapes());

        Assert.Equal(ItemPlace.Worn(GearRules.RightHand, 0), Item(inv, "Voulge").Place);
        Assert.Equal(ItemPlace.Worn(GearRules.RightHand, 1), Item(inv, "Guard Crossbow").Place);
        Assert.Equal(ItemPlace.Worn(GearRules.LeftHand, 1), Item(inv, "o_inv_leafshaped_bolts").Place);
        inv.Move(Index(inv, "Voulge"), ItemPlace.Worn(GearRules.RightHand, 0));
        Assert.Equal(0, s.ChangeCount);
    }

    [Fact]
    public void A_drop_that_changes_nothing_is_known_before_moving()
    {
        var (s, inv) = Open();

        Assert.False(inv.WouldMove(Index(inv, "Flat Kettle Helmet"), ItemPlace.Worn(GearRules.Head)));
        Assert.False(inv.WouldMove(Index(inv, "Voulge"), ItemPlace.Worn(GearRules.LeftHand, 0))); // its own set: the right hand
        Assert.True(inv.WouldMove(Index(inv, "Velmir Ring"), ItemPlace.Worn(GearRules.Ring2)));
        Assert.Equal(0, s.ChangeCount);
    }

    [Fact]
    public void A_save_with_two_items_in_one_slot_still_moves()
    {
        var (s, inv) = Open();
        Entry(s, Index(inv, "Fang Pendant")).Items[SlotIndex] = GmString.From(GearRules.Head);

        Assert.Null(inv.Refusal(Index(inv, "Velmir Ring"), ItemPlace.Worn(GearRules.Ring2)));
    }

    [Fact]
    public void Shields_can_go_beside_a_one_handed_weapon_only()
    {
        var (s, inv) = Open();
        var spot = inv.FirstFit(1, 1)!.Value;
        var shield = TestGear(s, inv, "Board Shield", spot.X, spot.Y);

        Assert.Equal("The other hand holds a two-handed weapon", inv.Refusal(shield, ItemPlace.Worn(GearRules.LeftHand, 0)));
        ToBag(inv, "o_inv_leafshaped_bolts");
        inv.Move(Index(inv, "Guard Crossbow"), ItemPlace.Worn(GearRules.RightHand, 0));
        Assert.Equal("The other hand holds a two-handed weapon", inv.Refusal(shield, ItemPlace.Worn(GearRules.LeftHand, 0)));
    }
}

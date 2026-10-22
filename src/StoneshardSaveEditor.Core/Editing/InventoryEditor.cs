using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Core.Editing;

/// <summary>Footprints and icons come from the game files; Core only knows item names.</summary>
public interface IItemShapeProvider
{
    (int W, int H) Footprint(string itemName);
    string SpriteFor(string itemName);
}

public sealed record ItemView(
    int Index, string Name, bool Equipped, string Slot,
    int? X, int? Y, int W, int H, int? WeaponSet,
    double? Charges, double? Stack, double? Durability, double? MaxDurability,
    bool IsGear, string Sprite);

/// <summary>
/// The character's inventory (<c>inventoryDataList</c>). Each entry is
/// <c>[name, props, f2, cell, f4, chargesMirror, f6, f7, weaponSet, slot]</c>; bag items have slot "N/A" and a cell
/// numbered <c>y * 10 + x</c>.
/// </summary>
public sealed class InventoryEditor(SaveSession session, IItemShapeProvider shapes)
{
    public const int Columns = 10;

    /// <summary>Rows known to exist from Velmir's save (items reach row 4); confirmed in-game in Task 12.</summary>
    public const int BagRows = 5;

    private const string BagSlot = "N/A";
    private const string Moneybag = "o_inv_moneybag";
    private const int NameIndex = 0, PropsIndex = 1, F2Index = 2, CellIndex = 3, F4Index = 4, ChargesIndex = 5,
        F6Index = 6, F7Index = 7, WeaponSetIndex = 8, SlotIndex = 9, EntryLength = 10;

    private GmArray List => session.Root["inventoryDataList"] as GmArray
                            ?? throw new EditRejectedException("This save has no inventory");

    public IReadOnlyList<ItemView> Read() => List.Items.Select((node, i) => View(i, (GmArray)node)).ToList();

    public int Gold() => (int)Read().Where(i => i.Name == Moneybag && !i.Equipped).Sum(i => i.Stack ?? 0);

    public (int X, int Y)? FirstFit(int w, int h) => Grid().FirstFit(w, h);

    public void SetCharges(int index, int value)
    {
        var view = ViewAt(index);
        if (view.Charges is null)
            throw new EditRejectedException("This item has no charges");
        Limits.Check("Charges", value, 1, 99, whole: true);
        var entry = EntryAt(index);
        Props(entry).Set("charge", GmNumber.From(value));
        entry.Items[ChargesIndex] = GmNumber.From(value);
        session.MarkChanged();
    }

    public void SetStack(int index, int value)
    {
        if (ViewAt(index).Stack is null)
            throw new EditRejectedException("This item doesn't stack");
        Limits.Check("Amount", value, 0, 10_000_000, whole: true);
        Props(EntryAt(index)).Set("Stack", GmNumber.From(value));
        session.MarkChanged();
    }

    public void SetDurability(int index, double current, double max)
    {
        if (ViewAt(index).MaxDurability is null)
            throw new EditRejectedException("This item has no durability");
        Limits.Check("Maximum durability", max, 1, 9_999, whole: true);
        Limits.Check("Durability", current, 0, max, whole: false);
        var props = Props(EntryAt(index));
        props.Set("Duration", GmNumber.From(current));
        props.Set("MaxDuration", GmNumber.From(max));
        session.MarkChanged();
    }

    public void Delete(int index)
    {
        EntryAt(index);
        List.Items.RemoveAt(index);
        session.MarkChanged();
    }

    public int Duplicate(int index) => Add((GmArray)EntryAt(index).DeepClone());

    /// <summary>Puts an entry into the first gap of the bag that fits it, converting it to the bag form.</summary>
    public int Add(GmArray entry)
    {
        if (entry.Items.Count != EntryLength || entry.Items[NameIndex] is not GmString name || entry.Items[PropsIndex] is not GmObject)
            throw new EditRejectedException("That item entry is malformed");
        var (w, h) = shapes.Footprint(name.Value);
        var spot = FirstFit(w, h) ?? throw new EditRejectedException("No room in the bag");

        entry.Items[F2Index] = GmNumber.From(0);
        entry.Items[CellIndex] = GmNumber.From(spot.Y * Columns + spot.X);
        entry.Items[F6Index] = GmNumber.From(-4);
        entry.Items[F7Index] = GmNumber.From(0);
        entry.Items[WeaponSetIndex] = GmNumber.From(0);
        entry.Items[SlotIndex] = GmString.From(BagSlot);
        if (entry.Items[F4Index] is not GmNumber)
            entry.Items[F4Index] = GmNumber.From(0);

        List.Items.Add(entry);
        session.MarkChanged();
        return List.Items.Count - 1;
    }

    public void SetGold(int amount, Func<GmArray> newMoneybag)
    {
        Limits.Check("Gold", amount, 0, 10_000_000, whole: true);
        var bags = Read().Where(i => i.Name == Moneybag && !i.Equipped).Select(i => i.Index).ToList();

        if (amount == 0)
        {
            foreach (var i in bags.OrderDescending())
                List.Items.RemoveAt(i);
            if (bags.Count > 0)
                session.MarkChanged();
            return;
        }

        if (bags.Count == 0)
        {
            var created = newMoneybag();
            Props(created).Set("Stack", GmNumber.From(amount));
            Add(created);
            return;
        }

        Props(EntryAt(bags[0])).Set("Stack", GmNumber.From(amount));
        foreach (var i in bags.Skip(1).OrderDescending())
            List.Items.RemoveAt(i);
        session.MarkChanged();
    }

    private BagGrid Grid()
    {
        var grid = new BagGrid(Columns, BagRows);
        foreach (var item in Read().Where(i => !i.Equipped && i.X is not null))
            grid.Occupy(item.X!.Value, item.Y!.Value, item.W, item.H);
        return grid;
    }

    private ItemView View(int index, GmArray entry)
    {
        var name = (entry.Items[NameIndex] as GmString)?.Value ?? "";
        var props = entry.Items[PropsIndex] as GmObject ?? new GmObject();
        var slot = (entry.Items[SlotIndex] as GmString)?.Value ?? BagSlot;
        var equipped = slot != BagSlot;
        var cell = entry.Items[CellIndex] is GmNumber c ? (int)c.Value : -1;
        var (w, h) = shapes.Footprint(name);
        var isGear = props["Metatype"] is not null;
        var stack = props["Stack"] is GmNumber s ? s.Value : (double?)null;
        var charge = props["charge"] is GmNumber ch ? ch.Value : 0;
        var max = props["MaxDuration"] is GmNumber m && m.Value > 0 ? m.Value : (double?)null;

        return new ItemView(
            index, name, equipped, slot,
            equipped || cell < 0 ? null : cell % Columns,
            equipped || cell < 0 ? null : cell / Columns,
            w, h,
            equipped && entry.Items[WeaponSetIndex] is GmNumber set ? (int)set.Value : null,
            !isGear && stack is null && charge >= 1 ? charge : null,
            stack,
            max is null ? null : (props["Duration"] as GmNumber)?.Value ?? 0,
            max,
            isGear,
            shapes.SpriteFor(name));
    }

    private ItemView ViewAt(int index) => View(index, EntryAt(index));

    private GmArray EntryAt(int index) =>
        index >= 0 && index < List.Items.Count && List.Items[index] is GmArray { Items.Count: EntryLength } entry
            ? entry
            : throw new EditRejectedException("That item no longer exists");

    private static GmObject Props(GmArray entry) => (GmObject)entry.Items[PropsIndex];
}

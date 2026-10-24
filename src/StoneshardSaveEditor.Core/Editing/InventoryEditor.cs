using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Core.Editing;

/// <summary>Footprints, icons and stack limits come from the game files; Core only knows item names.</summary>
public interface IItemShapeProvider
{
    (int W, int H) Footprint(string itemName);
    string SpriteFor(string itemName);

    /// <summary>How many of the item fit in one stack (gold coins 100, bolts 20); 0 when it doesn't stack.</summary>
    int MaxStack(string itemName);

    /// <summary>
    /// The item's kind from the game tables: a weapon's or armor's <c>Slot</c> (<c>sword</c>, <c>2hsword</c>, <c>Head</c>,
    /// <c>shield</c>, <c>Ring</c>, …) or another item's <c>Cat</c> (<c>ammo</c>, <c>tool</c>, <c>food</c>, …); "" when the
    /// tables don't list it.
    /// </summary>
    string Kind(string itemName);
}

public sealed record ItemView(
    int Index, string Name, bool Equipped, string Slot,
    int? X, int? Y, int W, int H, int? WeaponSet,
    double? Charges, double? MaxCharges, double? Stack, double? Count, double? Durability, double? MaxDurability,
    bool IsGear, string Sprite)
{
    /// <summary>Where it sits; only the hands have weapon sets, whatever a save records for other slots.</summary>
    public ItemPlace Place => Equipped
        ? ItemPlace.Worn(Slot, Slot is GearRules.RightHand or GearRules.LeftHand ? WeaponSet ?? 0 : 0)
        : ItemPlace.InBag(X ?? -1, Y ?? -1);
}

/// <summary>
/// The character's inventory (<c>inventoryDataList</c>). Each entry is
/// <c>[name, props, f2, cell, f4, chargesMirror, count, f7, weaponSet, slot]</c>; bag items have slot "N/A" and a cell
/// numbered <c>y * 10 + x</c>. <c>count</c> is the stack size of stackable items (gold coins, ammo) and -4 otherwise.
/// </summary>
public sealed class InventoryEditor(SaveSession session, IItemShapeProvider shapes)
{
    public const int Columns = 10;

    /// <summary>Rows known to exist from Velmir's save (items reach row 4); confirmed in-game in Task 12.</summary>
    public const int BagRows = 5;

    /// <summary>A purse is a 5 × 4 grid (owner, session B); gold coins and valuables such as gems share it.</summary>
    public const int PurseSlots = PurseColumns * PurseRows;

    private const int PurseColumns = 5, PurseRows = 4, CoinsPerStack = 100;
    private const string BagSlot = "N/A";
    private const string Moneybag = "o_inv_moneybag";
    private const string Coin = "o_inv_gold";
    private const int NameIndex = 0, PropsIndex = 1, F2Index = 2, CellIndex = 3, F4Index = 4, ChargesIndex = 5,
        CountIndex = 6, F7Index = 7, WeaponSetIndex = 8, SlotIndex = 9, EntryLength = 10;

    private Dictionary<string, double>? _maxCharges;

    /// <summary>The most charges seen for each item anywhere in the save, worked out once per editor.</summary>
    private Dictionary<string, double> MaxChargesSeen => _maxCharges ??= SaveItems.MaxCharges(session.Root);

    private GmArray List => session.Root["inventoryDataList"] as GmArray
                            ?? throw new EditRejectedException("This save has no inventory");

    public IReadOnlyList<ItemView> Read() => List.Items.Select((node, i) => View(i, (GmArray)node)).ToList();

    /// <summary>
    /// The gold the character really has: coin stacks inside purses and loose in the bag. A purse's <c>Stack</c> is only
    /// a cached total, which the game recounts from the coins inside when the purse is opened (session B).
    /// </summary>
    public int Gold() => Purses().Sum(i => CoinsIn(EntryAt(i))) + LooseCoins();

    /// <summary>Purses whose cached total differs from the coins inside them; the game shows the coins once opened.</summary>
    public IReadOnlyList<(int Index, int Claimed, int Coins)> StalePurses() =>
        Purses()
            .Select(i => (Index: i, Claimed: (int)((Props(EntryAt(i))["Stack"] as GmNumber)?.Value ?? 0), Coins: CoinsIn(EntryAt(i))))
            .Where(p => p.Claimed != p.Coins)
            .ToList();

    public (int X, int Y)? FirstFit(int w, int h) => Grid().FirstFit(w, h);

    public void SetCharges(int index, int value)
    {
        var view = ViewAt(index);
        if (view.Charges is null)
            throw new EditRejectedException("This item has no charges");
        Limits.Check("Charges", value, 1, view.MaxCharges ?? view.Charges.Value, whole: true);
        var entry = EntryAt(index);
        Props(entry).Set("charge", GmNumber.From(value));
        entry.Items[ChargesIndex] = GmNumber.From(value);
        session.MarkChanged();
    }

    public void SetCount(int index, int value)
    {
        var view = ViewAt(index);
        if (view.Count is null)
            throw new EditRejectedException("This item doesn't stack");
        Limits.Check("Count", value, 1, shapes.MaxStack(view.Name), whole: true);
        EntryAt(index).Items[CountIndex] = GmNumber.From(value);
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

    /// <summary>Why <paramref name="index"/> can't move to <paramref name="to"/>, or null when it can (spec §5).</summary>
    public string? Refusal(int index, ItemPlace to) => PlanMove(index, to).Refusal;

    /// <summary>Whether moving <paramref name="index"/> to <paramref name="to"/> is allowed and changes anything.</summary>
    public bool WouldMove(int index, ItemPlace to) => PlanMove(index, to) is { Refusal: null, Steps.Count: > 0 };

    /// <summary>
    /// Moves an item within the bag, onto an equipment slot or off it, making room as the game does (session H). An item
    /// already in the target slot trades places when it fits the mover's place, otherwise comes off. A two-handed item
    /// takes the other hand off, and an item beside a two-handed one takes that off; ammunition comes off as soon as its
    /// weapon leaves the hand. What comes off goes into the bag (the first one tries the spot the mover left), and what
    /// finds no room is removed and named. Only the placement fields change; the items' own data stays.
    /// </summary>
    public MoveResult Move(int index, ItemPlace to)
    {
        var plan = PlanMove(index, to);
        if (plan.Refusal is { } why)
            throw new EditRejectedException(why);
        if (plan.Steps.Count == 0)
            return new MoveResult(index, []);

        var mover = EntryAt(index);
        var active = ActiveSet();
        var moves = plan.Steps.Select(s => (Entry: EntryAt(s.Index), s.To)).ToList();
        foreach (var (entry, place) in moves)
            Relocate(entry, place, active);
        var gone = plan.Remove.Select(EntryAt).ToList();
        foreach (var entry in gone)
            List.Items.Remove(entry);
        session.MarkChanged();
        return new MoveResult(List.Items.IndexOf(mover), gone.Select(NameOf).ToList());
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
        if (shapes.MaxStack(name.Value) == 0 || entry.Items[CountIndex] is not GmNumber { Value: >= 0 })
            entry.Items[CountIndex] = GmNumber.From(-4);
        entry.Items[F7Index] = GmNumber.From(0);
        entry.Items[WeaponSetIndex] = GmNumber.From(0);
        entry.Items[SlotIndex] = GmString.From(BagSlot);
        if (entry.Items[F4Index] is not GmNumber)
            entry.Items[F4Index] = GmNumber.From(0);

        List.Items.Add(entry);
        session.MarkChanged();
        return List.Items.Count - 1;
    }

    /// <summary>
    /// Sets the gold carried. Loose coin stacks stay as they are; the rest goes into purses as stacks of 100, filling the
    /// purses in the bag in order and adding new ones (from <paramref name="newPurse"/>) when they are full. Valuables in a
    /// purse stay where they are. Nothing changes when the gold doesn't fit.
    /// </summary>
    public void SetGold(int amount, Func<GmArray> newPurse, Func<GmArray> newCoin)
    {
        Limits.Check("Gold", amount, 0, 10_000_000, whole: true);
        var loose = LooseCoins();
        if (amount < loose)
            throw new EditRejectedException($"You carry {loose} gold as loose coins; delete some in the Inventory tab to go lower");

        var left = amount - loose;
        var plan = new List<(int Index, int Amount)>();
        foreach (var i in Purses())
        {
            var take = Math.Min(left, Capacity(EntryAt(i)));
            plan.Add((i, take));
            left -= take;
        }

        const int fullPurse = PurseSlots * CoinsPerStack;
        var extra = (left + fullPurse - 1) / fullPurse;
        var grid = Grid();
        var (w, h) = shapes.Footprint(Moneybag);
        for (var k = 0; k < extra; k++)
        {
            var spot = grid.FirstFit(w, h)
                       ?? throw new EditRejectedException($"{amount} gold needs {extra} more purses, but the bag has room for {k}");
            grid.Occupy(spot.X, spot.Y, w, h);
        }

        foreach (var (i, take) in plan)
            Fill(EntryAt(i), take, newCoin);
        while (left > 0)
        {
            var purse = newPurse();
            Props(purse).Set("lootList", new GmArray());
            var take = Math.Min(left, fullPurse);
            Fill(purse, take, newCoin);
            Add(purse);
            left -= take;
        }
        session.MarkChanged();
    }

    /// <summary>Sets the gold inside one purse, as stacks of 100 in its free slots.</summary>
    public void SetPurseGold(int index, int amount, Func<GmArray> newCoin)
    {
        if (ViewAt(index) is not { Name: Moneybag, Equipped: false })
            throw new EditRejectedException("That isn't a purse");
        Limits.Check("Gold", amount, 0, 10_000_000, whole: true);
        Fill(EntryAt(index), amount, newCoin);
        session.MarkChanged();
    }

    private sealed record MovePlan(string? Refusal, IReadOnlyList<(int Index, ItemPlace To)> Steps, IReadOnlyList<int> Remove)
    {
        public static readonly MovePlan Nothing = new(null, [], []);

        public static MovePlan Refused(string why) => new(why, [], []);
    }

    private static readonly int[] WeaponSets = [0, 1];

    private MovePlan PlanMove(int index, ItemPlace to)
    {
        var item = ViewAt(index);
        var kind = shapes.Kind(item.Name);
        var from = item.Place;
        if (!to.IsBag && !IsHand(to.Slot))
            to = ItemPlace.Worn(to.Slot);
        else if (to.Slot == GearRules.LeftHand && GearRules.IsTwoHanded(kind))
            to = ItemPlace.Worn(GearRules.RightHand, to.WeaponSet); // the game takes a two-handed item on either hand (session G2)
        if (!to.IsBag && IsHand(to.Slot) && to.WeaponSet is not (0 or 1))
            return MovePlan.Refused("That doesn't go there");
        if (from == to)
            return MovePlan.Nothing;

        var all = Read();
        var worn = all.Where(i => i.Equipped && i.Index != index).GroupBy(i => i.Place).ToDictionary(g => g.Key, g => g.First());
        var rightBefore = WeaponSets.ToDictionary(set => set, set => all.FirstOrDefault(i => i.Equipped && i.Place == RightOf(set))?.Index);
        var steps = new List<(int Index, ItemPlace To)> { (index, to) };
        var off = new List<ItemView>();
        void TakeOff(ItemPlace place)
        {
            if (worn.Remove(place, out var gone))
                off.Add(gone);
        }
        // Ammunition comes off as soon as its weapon leaves the hand (session H), unless it is what is moving.
        void AmmoOff(int set)
        {
            if (worn.GetValueOrDefault(LeftOf(set)) is { } left && left.Index != index && shapes.Kind(left.Name) == "ammo")
                TakeOff(LeftOf(set));
        }
        if (from.Slot == GearRules.RightHand)
            AmmoOff(from.WeaponSet);

        if (to.IsBag)
        {
            if (!Grid(except: index).Fits(to.X, to.Y, item.W, item.H))
                return MovePlan.Refused("There's no room there");
        }
        else
        {
            if (!GearRules.SlotsFor(kind, item.Name).Contains(to.Slot))
                return MovePlan.Refused("That doesn't go there");
            if (kind == "ammo" && !(worn.GetValueOrDefault(RightOf(to.WeaponSet)) is { } shooter && GearRules.Shoots(shapes.Kind(shooter.Name), item.Name)))
                return MovePlan.Refused("Ammunition goes with a bow, crossbow or sling in the other hand");

            var occupant = worn.GetValueOrDefault(to);
            worn[to] = item;
            if (occupant is not null)
            {
                // The item already there trades places if it fits the mover's, else comes off.
                if (!from.IsBag && !worn.ContainsKey(from) && GearRules.SlotsFor(shapes.Kind(occupant.Name), occupant.Name).Contains(from.Slot))
                {
                    worn[from] = occupant;
                    if (HandRefusal(occupant, from, worn) is null)
                        steps.Add((occupant.Index, from));
                    else
                    {
                        worn.Remove(from);
                        off.Add(occupant);
                    }
                }
                else
                    off.Add(occupant);
            }

            // A two-handed item takes the other hand off; an item beside a two-handed one takes that off (session H).
            if (IsHand(to.Slot))
            {
                var otherPlace = to.Slot == GearRules.RightHand ? LeftOf(to.WeaponSet) : RightOf(to.WeaponSet);
                if (worn.GetValueOrDefault(otherPlace) is { } other)
                {
                    var otherKind = shapes.Kind(other.Name);
                    var clash = to.Slot == GearRules.RightHand
                        ? GearRules.IsTwoHanded(kind) && !(otherKind == "ammo" && GearRules.Shoots(kind, other.Name))
                        : GearRules.IsTwoHanded(otherKind) && !(kind == "ammo" && GearRules.Shoots(otherKind, item.Name));
                    if (clash)
                        TakeOff(otherPlace);
                }
            }
        }
        foreach (var set in WeaponSets)
            if (rightBefore[set] is { } was && worn.GetValueOrDefault(RightOf(set))?.Index != was)
                AmmoOff(set);

        // What comes off goes into the bag; the first item tries the spot the mover left.
        var grid = Grid(except: index);
        if (to.IsBag)
            grid.Occupy(to.X, to.Y, item.W, item.H);
        var remove = new List<int>();
        for (var k = 0; k < off.Count; k++)
        {
            var o = off[k];
            (int X, int Y)? spot = k == 0 && from.IsBag && grid.Fits(from.X, from.Y, o.W, o.H) ? (from.X, from.Y) : grid.FirstFit(o.W, o.H);
            if (spot is { } s)
            {
                grid.Occupy(s.X, s.Y, o.W, o.H);
                steps.Add((o.Index, ItemPlace.InBag(s.X, s.Y)));
            }
            else
                remove.Add(o.Index);
        }
        return new MovePlan(null, steps, remove);
    }

    private static ItemPlace RightOf(int set) => ItemPlace.Worn(GearRules.RightHand, set);

    private static ItemPlace LeftOf(int set) => ItemPlace.Worn(GearRules.LeftHand, set);

    /// <summary>Why <paramref name="item"/> can't be held at <paramref name="at"/> next to the set's other hand, or null.</summary>
    private string? HandRefusal(ItemView item, ItemPlace at, Dictionary<ItemPlace, ItemView> worn)
    {
        if (!IsHand(at.Slot))
            return null;
        var otherHand = at.Slot == GearRules.RightHand ? GearRules.LeftHand : GearRules.RightHand;
        var other = worn.GetValueOrDefault(ItemPlace.Worn(otherHand, at.WeaponSet));
        return GearRules.HandRefusal(shapes.Kind(item.Name), item.Name, at.Slot, other is null ? null : (shapes.Kind(other.Name), other.Name));
    }

    private static bool IsHand(string slot) => slot is GearRules.RightHand or GearRules.LeftHand;

    /// <summary>The weapon set in use: the one whose hands the game marks active (<c>f7</c>), set 0 when none is.</summary>
    private int ActiveSet() =>
        List.Items.OfType<GmArray>()
            .Where(e => e.Items.Count == EntryLength && IsHand((e.Items[SlotIndex] as GmString)?.Value ?? "") && IsTrue(e.Items[F7Index]))
            .Select(SetOf)
            .FirstOrDefault();

    /// <summary>Writes an entry's placement fields as the game does for a bag item or a worn one.</summary>
    private static void Relocate(GmArray entry, ItemPlace to, int activeSet)
    {
        if (to.IsBag)
        {
            entry.Items[F2Index] = GmNumber.From(0);
            entry.Items[CellIndex] = GmNumber.From(to.Y * Columns + to.X);
            entry.Items[F7Index] = GmNumber.From(0);
            entry.Items[WeaponSetIndex] = GmNumber.From(0);
            entry.Items[SlotIndex] = GmString.From(BagSlot);
            return;
        }
        var hand = IsHand(to.Slot);
        var set = hand ? to.WeaponSet : 0;
        entry.Items[F2Index] = GmNumber.From(-4);
        entry.Items[CellIndex] = GmNumber.From(-4);
        // Worn gear is marked in use; in the active weapon set the game marks the left hand 2 (Jorgrim's off-hand axe,
        // character_7's sling stones), and the other set's hands 0.
        entry.Items[F7Index] = !hand ? new GmBool(true)
            : set != activeSet ? GmNumber.From(0)
            : to.Slot == GearRules.LeftHand ? GmNumber.From(2)
            : new GmBool(true);
        entry.Items[WeaponSetIndex] = GmNumber.From(set);
        entry.Items[SlotIndex] = GmString.From(to.Slot);
    }

    /// <summary>The game writes the weapon set as a number or, in some saves, as true/false.</summary>
    private static int SetOf(GmArray entry) => entry.Items[WeaponSetIndex] switch
    {
        GmNumber n => (int)n.Value,
        GmBool b => b.Value ? 1 : 0,
        _ => 0,
    };

    private static bool IsTrue(GmNode node) => node is GmBool { Value: true } or GmNumber { Value: not 0 };

    private IEnumerable<int> Purses() =>
        List.Items.Select((node, i) => (node, i))
            .Where(x => x.node is GmArray e && NameOf(e) == Moneybag && InBag(e))
            .Select(x => x.i);

    private int LooseCoins() =>
        List.Items.OfType<GmArray>().Where(e => NameOf(e) == Coin && InBag(e)).Sum(CountOf);

    private static int CoinsIn(GmArray purse) => Loot(purse)?.Items.OfType<GmArray>().Where(e => NameOf(e) == Coin).Sum(CountOf) ?? 0;

    private int Capacity(GmArray purse) => FreeCells(purse).Count * CoinsPerStack;

    /// <summary>Replaces a purse's coins with <paramref name="amount"/> gold in stacks of 100, and its cached total.</summary>
    private void Fill(GmArray purse, int amount, Func<GmArray> newCoin)
    {
        var free = FreeCells(purse);
        var stacks = (amount + CoinsPerStack - 1) / CoinsPerStack;
        if (stacks > free.Count)
            throw new EditRejectedException($"A purse holds at most {free.Count * CoinsPerStack} gold");

        var props = Props(purse);
        if (props["lootList"] is not GmArray loot)
            props.Set("lootList", loot = new GmArray());
        loot.Items.RemoveAll(n => n is GmArray e && NameOf(e) == Coin);
        for (var k = 0; k < stacks; k++)
        {
            var coin = newCoin();
            coin.Items[F2Index] = GmNumber.From(0);
            coin.Items[CellIndex] = GmNumber.From(free[k]);
            coin.Items[CountIndex] = GmNumber.From(Math.Min(CoinsPerStack, amount - k * CoinsPerStack));
            coin.Items[F7Index] = GmNumber.From(0);
            coin.Items[WeaponSetIndex] = GmNumber.From(0);
            coin.Items[SlotIndex] = GmString.From(BagSlot);
            loot.Items.Add(coin);
        }
        props.Set("Stack", GmNumber.From(amount));
    }

    /// <summary>Purse slots not taken by valuables, down each column first as the game places coins (cells 0, 5, 10, 15).</summary>
    private List<int> FreeCells(GmArray purse)
    {
        var taken = new HashSet<int>();
        foreach (var e in Loot(purse)?.Items.OfType<GmArray>().Where(e => NameOf(e) != Coin) ?? [])
        {
            if (e.Items.Count <= CellIndex || e.Items[CellIndex] is not GmNumber { Value: >= 0 } c)
                continue;
            var (w, h) = shapes.Footprint(NameOf(e));
            var (x0, y0) = ((int)c.Value % PurseColumns, (int)c.Value / PurseColumns);
            for (var x = x0; x < x0 + w; x++)
            for (var y = y0; y < y0 + h; y++)
                taken.Add(y * PurseColumns + x);
        }
        var free = new List<int>();
        for (var x = 0; x < PurseColumns; x++)
        for (var y = 0; y < PurseRows; y++)
            if (!taken.Contains(y * PurseColumns + x))
                free.Add(y * PurseColumns + x);
        return free;
    }

    private static GmArray? Loot(GmArray purse) => (purse.Items[PropsIndex] as GmObject)?["lootList"] as GmArray;

    private static string NameOf(GmArray entry) => (entry.Items.Count > NameIndex ? entry.Items[NameIndex] as GmString : null)?.Value ?? "";

    private static bool InBag(GmArray entry) =>
        entry.Items.Count == EntryLength && (entry.Items[SlotIndex] as GmString)?.Value == BagSlot;

    private static int CountOf(GmArray entry) =>
        entry.Items.Count > CountIndex && entry.Items[CountIndex] is GmNumber { Value: > 0 } n ? (int)n.Value : 0;

    private BagGrid Grid(int except = -1)
    {
        var grid = new BagGrid(Columns, BagRows);
        foreach (var item in Read().Where(i => !i.Equipped && i.X is not null && i.Index != except))
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
        // A purse's gold is the coins inside it; its own Stack is only a cached total (see Gold()).
        var stack = name == Moneybag ? CoinsIn(entry) : props["Stack"] is GmNumber s ? s.Value : (double?)null;
        var charge = props["charge"] is GmNumber ch ? ch.Value : 0;
        var max = props["MaxDuration"] is GmNumber m && m.Value > 0 ? m.Value : (double?)null;
        var count = shapes.MaxStack(name) > 0 && entry.Items[CountIndex] is GmNumber { Value: >= 0 } n ? n.Value : (double?)null;
        var charges = !isGear && stack is null && count is null && charge >= 1 ? charge : (double?)null;

        return new ItemView(
            index, name, equipped, slot,
            equipped || cell < 0 ? null : cell % Columns,
            equipped || cell < 0 ? null : cell / Columns,
            w, h,
            equipped ? SetOf(entry) : null,
            charges,
            charges is null ? null : Math.Max(charges.Value, MaxChargesSeen.GetValueOrDefault(name)),
            stack,
            count,
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

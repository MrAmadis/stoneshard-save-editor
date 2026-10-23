namespace StoneshardSaveEditor.Core.Editing;

/// <summary>Where an item sits: a backpack cell, or an equipment slot (hands come in weapon sets 0 and 1).</summary>
public sealed record ItemPlace(string Slot, int WeaponSet, int X, int Y)
{
    public const string BagSlot = "N/A";

    public static ItemPlace InBag(int x, int y) => new(BagSlot, 0, x, y);

    public static ItemPlace Worn(string slot, int weaponSet = 0) => new(slot, weaponSet, -1, -1);

    public bool IsBag => Slot == BagSlot;
}

/// <param name="Index">Where the moved item is in the inventory list afterwards.</param>
/// <param name="Removed">The item that came off with no room left in the bag and was removed (spec §5.3), or null.</param>
public sealed record MoveResult(int Index, string? Removed);

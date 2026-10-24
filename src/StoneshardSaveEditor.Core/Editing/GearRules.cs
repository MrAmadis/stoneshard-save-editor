namespace StoneshardSaveEditor.Core.Editing;

/// <summary>
/// Where the game lets an item be worn, by its kind from the game tables (see <see cref="IItemShapeProvider.Kind"/>).
/// Hands come in two weapon sets. A two-handed item takes a set's right hand and leaves the left free, except for the
/// ammunition it shoots (Velmir's crossbow and bolts). Session G2 showed slings, sickles and torches going in either hand
/// (a sling with stones in the other), and lutes, chains and pickaxes taking both. Wands are still unknown, as the
/// editor can't make one to check, so they can't be worn yet.
/// </summary>
public static class GearRules
{
    public const string Head = "o_inv_head", Body = "o_inv_armor", Hands = "o_inv_gloves", Feet = "o_inv_boots", Belt = "o_inv_belt",
        Neck = "o_inv_neck", Back = "o_inv_back", Ring1 = "o_inv_ring_1", Ring2 = "o_inv_ring_2",
        RightHand = "o_inv_right_hand", LeftHand = "o_inv_left_hand";

    private static readonly string[] TwoHanded = ["2hsword", "2haxe", "2hmace", "2hStaff", "spear", "bow", "crossbow", "lute", "chain", "pick"];
    private static readonly string[] OneHanded = ["sword", "axe", "mace", "dagger", "sling"];

    /// <summary>The torch is the one tool from the items table held in a hand; the rest (bombs, nets, bedrolls) aren't worn.</summary>
    private const string Torch = "o_inv_torch";

    public static IReadOnlyList<string> SlotsFor(string kind, string name) => kind switch
    {
        "Head" => [Head],
        "Chest" => [Body],
        "Arms" => [Hands],
        "Legs" => [Feet],
        "Waist" => [Belt],
        "Amulet" => [Neck],
        "Back" => [Back],
        "Ring" => [Ring1, Ring2],
        "shield" or "ammo" => [LeftHand],
        // Tools from the weapons table (the sickle) have names without the items table's prefix.
        "tool" when name == Torch || !name.StartsWith("o_inv_", StringComparison.Ordinal) => [RightHand, LeftHand],
        _ when OneHanded.Contains(kind) => [RightHand, LeftHand],
        _ when TwoHanded.Contains(kind) => [RightHand],
        _ => [],
    };

    public static bool IsTwoHanded(string kind) => TwoHanded.Contains(kind);

    /// <summary>Bows shoot arrows, crossbows bolts, and slings stones, bullets and metal shot.</summary>
    public static bool Shoots(string weaponKind, string ammoName) => weaponKind switch
    {
        "bow" => ammoName.Contains("arrows", StringComparison.Ordinal),
        "crossbow" => ammoName.Contains("bolts", StringComparison.Ordinal),
        "sling" => ammoName.Contains("ammo_", StringComparison.Ordinal),
        _ => false,
    };

    /// <summary>Why an item can't go in <paramref name="hand"/> next to what the set's other hand holds, or null.</summary>
    public static string? HandRefusal(string kind, string name, string hand, (string Kind, string Name)? other)
    {
        if (hand == RightHand)
            return IsTwoHanded(kind) && other is { } o && !(o.Kind == "ammo" && Shoots(kind, o.Name))
                ? "A two-handed weapon needs the other hand free"
                : null;
        if (other is { } held && IsTwoHanded(held.Kind) && !(kind == "ammo" && Shoots(held.Kind, name)))
            return "The other hand holds a two-handed weapon";
        if (kind == "ammo" && !(other is { } weapon && Shoots(weapon.Kind, name)))
            return "Ammunition goes with a bow, crossbow or sling in the other hand";
        return null;
    }
}

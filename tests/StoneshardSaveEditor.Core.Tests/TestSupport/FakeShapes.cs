using StoneshardSaveEditor.Core.Editing;

namespace StoneshardSaveEditor.Tests;

/// <summary>Velmir's real footprints (icon size / 27 px), so tests don't need the game install.</summary>
internal sealed class FakeShapes : IItemShapeProvider
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

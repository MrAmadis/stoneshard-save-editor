using StoneshardSaveEditor.Core.Editing;

namespace StoneshardSaveEditor.GameData;

/// <summary>
/// Icons and bag footprints for item names. Object items (<c>o_inv_*</c>) use the sprite their game object draws with;
/// generated gear uses <c>s_inv_</c> + its name in lowercase letters and digits ("Guard Crossbow" → s_inv_guardcrossbow).
/// </summary>
public sealed class GameItemShapes(SpriteTable sprites, ObjectTable objects) : IItemShapeProvider
{
    public const string Missing = "missing";
    private const int CellPixels = 27;

    public string SpriteFor(string itemName)
    {
        var name = itemName.StartsWith("o_", StringComparison.Ordinal) ? objects.SpriteOf(itemName) : GearSprite(itemName);
        return name is not null && sprites.Get(name) is not null ? name : Missing;
    }

    public (int W, int H) Footprint(string itemName)
    {
        var sprite = SpriteFor(itemName);
        if (sprite == Missing)
            return (1, 1);
        var (w, h) = sprites.Get(sprite)!.CanvasSize;
        return (Math.Max(1, (w + CellPixels - 1) / CellPixels), Math.Max(1, (h + CellPixels - 1) / CellPixels));
    }

    public static string GearSprite(string gearName) =>
        "s_inv_" + new string(gearName.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

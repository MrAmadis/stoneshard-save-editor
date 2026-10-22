using System.Globalization;
using StoneshardSaveEditor.Core.Editing;

namespace StoneshardSaveEditor.UI.Services;

/// <summary>Sizes for drawing items: the game's 27 px bag cells shown at 2×.</summary>
public static class InventoryLayout
{
    public const int Cell = 54;
    private const int GamePixels = 27;

    /// <summary>An icon of <paramref name="w"/> × <paramref name="h"/> cells at 2×, shrunk if needed to fit the box.</summary>
    public static (int W, int H) Fit(int w, int h, double boxW, double boxH)
    {
        var scale = Math.Min(2.0, Math.Min(boxW / (w * GamePixels), boxH / (h * GamePixels)));
        return ((int)(w * GamePixels * scale), (int)(h * GamePixels * scale));
    }

    /// <summary>The number drawn on an item, as in the game: coins, stack size, or uses left when more than one.</summary>
    public static string? Badge(ItemView item)
    {
        var value = (item.Stack > 0 ? item.Stack : null) ?? item.Count ?? (item.Charges > 1 ? item.Charges : null);
        return value?.ToString("0.##", CultureInfo.InvariantCulture);
    }
}

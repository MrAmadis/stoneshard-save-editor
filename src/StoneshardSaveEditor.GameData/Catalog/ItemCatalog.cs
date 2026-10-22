using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.GameData;

public enum CatalogAvailability
{
    /// <summary>Built from the game's tables with rules proven against real saves.</summary>
    Ready,
    /// <summary>Not buildable from tables, but a copy exists in this save to clone.</summary>
    CloneFromSave,
    Unavailable,
}

public sealed record CatalogItem(
    string Key, string DisplayName, string Category, string Sprite, int W, int H,
    CatalogAvailability Availability, string? Reason);

/// <summary>Every item the game defines, with whether the editor can add it to this save.</summary>
public sealed class ItemCatalog(ExeTables tables, Localization localization, SpriteTable sprites, ObjectTable objects)
{
    private const string ObjectPrefix = "o_inv_";
    private const string NoIcon = "No icon in game files";
    private const string NoData = "Item data not readable from game files yet";

    private readonly GameItemShapes _shapes = new(sprites, objects);

    public IReadOnlyList<CatalogItem> All(GmObject saveRoot)
    {
        var inSave = ItemPropsBuilder.Instances(saveRoot).Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
        var result = new List<CatalogItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (table, isWeapon) in new[] { (tables.Weapons, true), (tables.Armor, false) })
        foreach (var row in table.Rows)
        {
            if (!seen.Add(row.Key))
                continue;
            var buildable = ItemPropsBuilder.FromGearRow(row, isWeapon) is not null;
            result.Add(Describe(row.Key, localization.English(row.Key) ?? row.Key, row["Slot"],
                buildable ? CatalogAvailability.Ready : inSave.Contains(row.Key) ? CatalogAvailability.CloneFromSave : CatalogAvailability.Unavailable));
        }

        foreach (var row in tables.ItemsStats.Rows)
        {
            var key = ObjectPrefix + row.Key;
            if (!objects.Exists(key) || !seen.Add(key))
                continue;
            var availability = ItemPropsBuilder.FromItemsStatsRow(row) is not null ? CatalogAvailability.Ready
                : inSave.Contains(key) ? CatalogAvailability.CloneFromSave
                : CatalogAvailability.Unavailable;
            result.Add(Describe(key, localization.English(row.Key) ?? row.Key, row["Cat"], availability));
        }
        return result;
    }

    /// <summary>A complete inventory entry for a brand-new copy of the item, ready for <see cref="InventoryEditor.Add"/>.</summary>
    public GmArray CreateEntry(string key, GmObject saveRoot)
    {
        var props = BuildProps(key, saveRoot)
                    ?? throw new EditRejectedException($"{localization.English(key.Replace(ObjectPrefix, "")) ?? key} can't be added: {NoData.ToLowerInvariant()}");
        if (_shapes.SpriteFor(key) == GameItemShapes.Missing)
            throw new EditRejectedException($"{key} can't be added: {NoIcon.ToLowerInvariant()}");

        var charge = props["charge"] is GmNumber c ? c.Value : -4;
        var entry = new GmArray();
        entry.Items.AddRange(
        [
            GmString.From(key), props, GmNumber.From(0), GmNumber.From(-4), GmNumber.From(0),
            GmNumber.From(charge), GmNumber.From(-4), GmNumber.From(0), GmNumber.From(0), GmString.From("N/A"),
        ]);
        return entry;
    }

    private GmObject? BuildProps(string key, GmObject saveRoot)
    {
        if (tables.Weapons.Find(key) is { } weapon)
            return ItemPropsBuilder.FromGearRow(weapon, isWeapon: true) ?? ItemPropsBuilder.CloneFromSave(key, saveRoot);
        if (tables.Armor.Find(key) is { } armor)
            return ItemPropsBuilder.FromGearRow(armor, isWeapon: false) ?? ItemPropsBuilder.CloneFromSave(key, saveRoot);
        if (!key.StartsWith(ObjectPrefix, StringComparison.Ordinal) || tables.ItemsStats.Find(key[ObjectPrefix.Length..]) is not { } row)
            return null;

        // A real copy from this save keeps fields the tables don't hold (a moneybag's contents, for instance).
        var props = ItemPropsBuilder.CloneFromSave(key, saveRoot) ?? ItemPropsBuilder.FromItemsStatsRow(row);
        if (props is null)
            return null;
        if (double.TryParse(row["Fresh"], System.Globalization.CultureInfo.InvariantCulture, out var fresh))
            props.Set("Fresh", GmNumber.From(fresh));
        if (props["charge"] is GmNumber { Value: >= 1 })
            props.Set("charge", GmNumber.From(ItemPropsBuilder.MaxObservedCharge(key, saveRoot) ?? 1));
        return props;
    }

    private CatalogItem Describe(string key, string displayName, string category, CatalogAvailability availability)
    {
        var sprite = _shapes.SpriteFor(key);
        if (sprite == GameItemShapes.Missing)
            return new CatalogItem(key, displayName, category, sprite, 1, 1, CatalogAvailability.Unavailable, NoIcon);
        var (w, h) = _shapes.Footprint(key);
        return new CatalogItem(key, displayName, category, sprite, w, h, availability,
            availability == CatalogAvailability.Unavailable ? NoData : null);
    }
}

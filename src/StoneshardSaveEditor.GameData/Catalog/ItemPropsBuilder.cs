using System.Globalization;
using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.GameData;

/// <summary>
/// Builds an item's property map the way the game does. The rules were derived from, and are tested against,
/// every item instance in the owner's saves (CatalogTests): gear matches 100%, and so do the item categories in
/// <see cref="BuildableCategories"/>. Tools, ammo and pot-served dishes get stats from game code, so they are not built.
/// </summary>
public static class ItemPropsBuilder
{
    private static readonly HashSet<string> NotStats =
    [
        "name", "Tier", "id", "Slot", "Subtype", "altGrip", "rarity", "Mat", "Price", "Markup", "Balance",
        "IsOpen", "class", "tags", "Rng", "audio", "upgrade", "MaxDuration",
    ];

    private static readonly HashSet<string> RangedSlots = ["bow", "crossbow", "sling"];

    private static readonly HashSet<string> BuildableCategories =
    [
        "additive", "alcohol", "backpack", "bag", "beverage", "book", "commodity", "currency", "drug", "food", "ingredient",
        "junk", "material", "medicine", "recipe", "resource", "scroll", "upgrade", "valuable",
    ];

    /// <summary>Per-copy markers that must not be carried over when cloning (ownership, theft).</summary>
    private static readonly string[] OwnershipKeys = ["HasOwner", "Stolen_Days", "Stolen_Days_Timestamp", "Town"];

    public static GmObject? FromGearRow(TableRow row, bool isWeapon)
    {
        if (!TryNumber(row["MaxDuration"], out var maxDuration))
            return null;
        var rarity = row["rarity"];
        double quality = rarity == "Unique" ? 6 : 1;

        var props = new GmObject();
        props.Set("idName", GmString.From(row.Key));
        props.Set("Metatype", GmString.From(isWeapon ? "Weapon" : "Armor"));
        props.Set("rarity", GmString.From(rarity));
        props.Set("tags", GmString.From(row["tags"]));
        props.Set("Material", GmString.From(row["Mat"]));
        props.Set("quality", GmNumber.From(quality));
        props.Set("Suffix", GmString.From($"{quality.ToString(CultureInfo.InvariantCulture)} {row["Slot"]}"));
        props.Set("MaxDuration", GmNumber.From(maxDuration));
        props.Set("Duration", GmNumber.From(maxDuration));
        props.Set("Curse", new GmArray());
        props.Set("is_cursed", GmNumber.From(0));
        props.Set("cursedQuality", GmNumber.From(-4));
        props.Set("identified", GmNumber.From(1));
        props.Set("charge", GmNumber.From(-4));
        props.Set("key", GmString.From(""));
        props.Set("Colour", GmNumber.From(rarity == "Unique" ? 12339330 : 16777215));

        var damage = new List<(string Type, double Value)>();
        foreach (var column in row.Columns)
        {
            if (column.Length == 0 || column.StartsWith('#') || NotStats.Contains(column) || column.StartsWith("fragment_", StringComparison.Ordinal))
                continue;
            var cell = row[column];
            if (cell.Length == 0)
                continue;
            if (!TryNumber(cell, out var value))
            {
                props.Set(column, GmString.From(cell));
                continue;
            }
            if (value == 0)
                continue;
            props.Set(column, GmNumber.From(value));
            if (column.EndsWith("_Damage", StringComparison.Ordinal) && column is not ("Armor_Damage" or "Bodypart_Damage"))
                damage.Add((column, value));
        }

        if (isWeapon)
        {
            if (damage.Count == 0 || !TryNumber(row["Rng"], out var range))
                return null;
            props.Set("Range", GmNumber.From(range));
            if (RangedSlots.Contains(row["Slot"]))
                props.Set("Rng", GmNumber.From(range));
            var main = damage.MaxBy(d => d.Value);
            props.Set("DMG", GmNumber.From(damage.Sum(d => d.Value)));
            props.Set("DamageType", GmString.From(main.Type));
            var mainList = new GmArray();
            foreach (var (type, value) in damage)
                mainList.Items.AddRange([GmString.From(type), GmNumber.From(value)]);
            props.Set("Main", mainList);
        }
        else
        {
            props.Set("Armor_Type", GmString.From(row["class"]));
            var def = props["DEF"] is GmNumber d ? d.Value : 0;
            props.Set("DEF", GmNumber.From(def));
            var main = new GmArray();
            if (def > 0)
                main.Items.AddRange([GmString.From("DEF"), GmNumber.From(def)]);
            props.Set("Main", main);
        }
        return props;
    }

    /// <summary>Props for a non-gear item, or null when its category's stats come from game code.</summary>
    public static GmObject? FromItemsStatsRow(TableRow row)
    {
        var category = row["Cat"];
        if (!BuildableCategories.Contains(category) || (category == "food" && row["is_container"] == "1") || row.Key == "bottle")
            return null;

        var props = new GmObject();
        props.Set("idName", GmString.From(row.Key));
        props.Set("Material", GmString.From(row["Material"]));
        props.Set("tags", GmString.From(row["tags"]));
        props.Set("Effects_Duration", GmNumber.From(TryNumber(row["Duration"], out var d) ? d : 0));
        props.Set("Duration", GmNumber.From(0));
        props.Set("MaxDuration", GmNumber.From(0));
        props.Set("Main", new GmArray());
        props.Set("identified", GmNumber.From(1));
        props.Set("is_cursed", GmNumber.From(0));
        props.Set("i_index", GmNumber.From(0));
        props.Set("charge", GmNumber.From(1));
        if (TryNumber(row["Fresh"], out var fresh))
            props.Set("Fresh", GmNumber.From(fresh));
        return props;
    }

    /// <summary>Copies the props of the first item called <paramref name="key"/> anywhere in the save, as new: repaired and unowned.</summary>
    public static GmObject? CloneFromSave(string key, GmObject saveRoot)
    {
        var source = Instances(saveRoot).FirstOrDefault(i => i.Name == key).Props;
        if (source is null)
            return null;
        var props = (GmObject)source.DeepClone();
        foreach (var k in OwnershipKeys)
            props.Remove(k);
        if (props["MaxDuration"] is GmNumber max && max.Value > 0)
            props.Set("Duration", GmNumber.From(max.Value));
        return props;
    }

    /// <summary>The highest charge count any copy of <paramref name="key"/> in the save has (merchant stock is usually full).</summary>
    internal static double? MaxObservedCharge(string key, GmObject saveRoot) =>
        SaveItems.MaxCharges(saveRoot).TryGetValue(key, out var max) ? max : null;

    /// <summary>Every <c>[name, props, …]</c> item entry anywhere in the save: inventory, stash, merchants, containers.</summary>
    internal static IEnumerable<(string Name, GmObject Props)> Instances(GmNode node) => SaveItems.Instances(node);

    private static bool TryNumber(string cell, out double value) =>
        double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

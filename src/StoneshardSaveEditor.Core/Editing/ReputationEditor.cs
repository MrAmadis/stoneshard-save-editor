using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Core.Editing;

/// <param name="VillageKey">The game's settlement key, e.g. <c>RottenWillow</c>.</param>
/// <param name="LocationKey">The world-map cell, e.g. <c>13_9</c>.</param>
public sealed record SettlementReputation(string VillageKey, string LocationKey, double Value);

/// <summary>
/// Reputation per settlement. Settlements are listed under <c>factionsDataMap.*.villages{name:{x,y}}</c>;
/// the value lives at <c>globalmapDataMap.locationsDataMap["x_y"].reputation</c>.
/// </summary>
public sealed class ReputationEditor(SaveSession session)
{
    /// <summary>The game's highest reputation (owner, final acceptance).</summary>
    public const double Max = 6000;

    public IReadOnlyList<SettlementReputation> Read()
    {
        var result = new List<SettlementReputation>();
        if (session.Root["factionsDataMap"] is not GmObject factions)
            return result;
        foreach (var (_, faction) in factions.Entries)
        {
            if ((faction as GmObject)?["villages"] is not GmObject villages)
                continue;
            foreach (var (name, cell) in villages.Entries)
            {
                if (cell is not GmObject xy || xy["x"] is not GmNumber x || xy["y"] is not GmNumber y)
                    continue;
                var key = $"{(int)x.Value}_{(int)y.Value}";
                if (result.Any(r => r.LocationKey == key))
                    continue;
                if (Location(key)?["reputation"] is GmNumber reputation)
                    result.Add(new SettlementReputation(name, key, reputation.Value));
            }
        }
        return result;
    }

    public void Set(string locationKey, double value)
    {
        if (Read().All(r => r.LocationKey != locationKey))
            throw new EditRejectedException($"No settlement with reputation at {locationKey}");
        Limits.Check("Reputation", value, 0, Max, whole: true);
        Location(locationKey)!.Set("reputation", GmNumber.From(value));
        session.MarkChanged();
    }

    private GmObject? Location(string key) =>
        ((session.Root["globalmapDataMap"] as GmObject)?["locationsDataMap"] as GmObject)?[key] as GmObject;
}

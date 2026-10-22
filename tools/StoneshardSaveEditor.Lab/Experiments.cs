using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Lab;

public sealed record ExperimentResult(string Folder, string Label, string Purpose);

/// <summary>
/// Builds the test characters for in-game session A (design §10, plan Task 12). Each is a re-signed copy of
/// Velmir's exit save with one experiment applied; the owner's real characters are never written.
/// </summary>
public static class Experiments
{
    public const string SourceCharacter = "character_2";
    public const string SourceSlot = "exitsave_1";

    private static readonly string[] BagJunk =
        ["o_inv_cheese", "o_inv_goatcheese_rot", "o_inv_antitoxin_empty", "o_inv_potion04_empty", "o_inv_morion"];

    public static IReadOnlyList<ExperimentResult> Build(string root, GameServices game, BackupService backups, IGameProcessProbe probe)
    {
        if (probe.IsGameRunning())
            throw new InvalidOperationException("Close Stoneshard before building experiment saves");

        var results = new List<ExperimentResult>();
        void Make(string label, string purpose, Action<SaveSession> apply)
        {
            var folder = TestCharacterCloner.Clone(root, SourceCharacter, SourceSlot, label, minutesLater: results.Count + 1);
            var slot = new SaveLocator(root).Scan().Single(c => c.Folder == folder).Slots.Single();
            var session = SaveSession.Open(slot);
            apply(session);
            if (session.ChangeCount > 0 && session.Save(backups, probe) is var result and not SaveResult.Saved)
                throw new InvalidOperationException($"Could not save experiment {label}: {result}");
            results.Add(new ExperimentResult(folder, label, purpose));
        }

        Make("Osbrook", "A0: untouched copy; should load exactly like Velmir", _ => { });
        Make("Brynn", "A1: STR 18, stat points 3, ability points 4, level 6, XP 2000, HP 999, Sanity 150, Hunger 0, Osbrook rep 9000, gold 99,999",
            s => Brynn(s, game));
        Make("Mannshire", "A2: salve 1 charge, wineskin 2, helmet 50/105, junk deleted, a salve duplicated, Noble Longsword + Copper Ring + antitoxin + Pyromancy Treatise I added, probe salves in rows 5-9",
            s => Mannshire(s, game));
        Make("Denbrie", "A3: 2 ability points; Hooking Chop unlearned; Killing Swing learned directly; Fatal Strike unlocked only",
            Denbrie);
        var closed = ClosedTreeTreatises(root, game);
        Make("RottenWillow", $"A4: vol I treatise for each closed tree ({closed.Count}): read all, quit, then screenshot every skill tree",
            s => FillBagWith(s, game, closed));
        Make("Brynn_NW", "A5: Pyromancy Treatise I-IV: read I, quit; reload, read II, quit; then III, then IV",
            s => FillBagWith(s, game, ["o_inv_treatise_pyro1", "o_inv_treatise_pyro2", "o_inv_treatise_pyro3", "o_inv_treatise_pyro4"]));
        return results;
    }

    private static void Brynn(SaveSession s, GameServices game)
    {
        var character = new CharacterEditor(s);
        character.Set(CharacterField.Strength, 18);
        character.Set(CharacterField.StatPoints, 3);
        character.Set(CharacterField.AbilityPoints, 4);
        character.Set(CharacterField.Level, 6);
        character.Set(CharacterField.Experience, 2000);
        character.Set(CharacterField.Health, 999);
        character.Set(CharacterField.Hunger, 0);
        // Deliberately outside the editor's limits, to see whether the game clamps it.
        ((GmObject)s.Root["characterDataMap"]!).Set("Sanity", GmNumber.From(150));
        s.MarkChanged();
        new ReputationEditor(s).Set("32_10", 9000);
        new InventoryEditor(s, game.Shapes).SetGold(99_999, () => game.Catalog.CreateEntry("o_inv_moneybag", s.Root));
    }

    private static void Mannshire(SaveSession s, GameServices game)
    {
        var inv = new InventoryEditor(s, game.Shapes);
        int Find(Func<ItemView, bool> match) => inv.Read().First(match).Index;

        inv.SetCharges(Find(i => i.Name == "o_inv_salve" && i.X == 5 && i.Y == 1), 1);
        inv.SetCharges(Find(i => i.Name == "o_inv_wineskin" && i.Charges == 6), 2);
        inv.SetDurability(Find(i => i.Name == "Flat Kettle Helmet"), 50, 105);
        foreach (var junk in BagJunk)
            inv.Delete(Find(i => i.Name == junk && !i.Equipped));
        inv.Duplicate(Find(i => i.Name == "o_inv_salve"));
        foreach (var key in new[] { "Noble Longsword", "Copper Ring", "o_inv_antitoxin", "o_inv_treatise_pyro1" })
            inv.Add(game.Catalog.CreateEntry(key, s.Root));

        // Probe how many bag rows exist: one salve at the start of rows 5 to 9.
        var list = (GmArray)s.Root["inventoryDataList"]!;
        foreach (var cell in new[] { 50, 60, 70, 80, 90 })
        {
            var entry = game.Catalog.CreateEntry("o_inv_salve", s.Root);
            entry.Items[3] = GmNumber.From(cell);
            list.Items.Add(entry);
        }
        s.MarkChanged();
    }

    private static void Denbrie(SaveSession s)
    {
        var skills = new SkillsEditor(s);
        new CharacterEditor(s).Set(CharacterField.AbilityPoints, 2);
        skills.Unlearn("o_skill_wide_cut_ico", refundPoint: false);
        skills.Learn("o_skill_killing_swing_ico", spendPoint: false);
        skills.Unlock(["o_pass_skill_fatal_hit"]);
    }

    /// <summary>Volume I of every tree that is still closed on Velmir: nothing learned and nothing unlocked.</summary>
    private static List<string> ClosedTreeTreatises(string root, GameServices game)
    {
        var file = Path.Combine(root, SourceCharacter, SourceSlot, "data.sav");
        var saveRoot = (GmObject)GmJson.Parse(SaveCodec.Decode(File.ReadAllBytes(file)).Json);
        var state = SkillsEditor.ReadFrom(saveRoot).ToDictionary(s => s.Object);
        return game.SkillsFor(saveRoot).Trees
            .Where(t => t.TreatisePrefix is not null && t.Skills.All(s => state[s.Object] is { Learned: false, Locked: true }))
            .Select(t => $"o_inv_treatise_{t.TreatisePrefix}1")
            .Where(key => game.Tables.ItemsStats.Find(key["o_inv_".Length..]) is not null)
            .ToList();
    }

    private static void FillBagWith(SaveSession s, GameServices game, IReadOnlyList<string> keys)
    {
        var inv = new InventoryEditor(s, game.Shapes);
        foreach (var item in inv.Read().Where(i => !i.Equipped).OrderByDescending(i => i.Index))
            inv.Delete(item.Index);
        foreach (var key in keys)
            inv.Add(game.Catalog.CreateEntry(key, s.Root));
    }
}

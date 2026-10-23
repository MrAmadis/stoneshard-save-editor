using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Lab;

public sealed record ExperimentResult(string Folder, string Label, string Purpose);

/// <summary>
/// Builds the test characters for the in-game sessions (design §10, plan Task 12). Each is a re-signed copy of
/// Velmir's latest save with one experiment applied, written as a regular save so loading it doesn't consume it; the
/// owner's real characters are never written.
/// </summary>
public static class Experiments
{
    public const string SourceCharacter = "character_2";

    /// <summary>Velmir's exit save, or his newest other save once the game has consumed it (loading an exit save does).</summary>
    public static string SourceSlot(string root) =>
        new SaveLocator(root).Scan().Single(c => c.Folder == SourceCharacter).Slots
            .Where(s => s.Status == SlotStatus.Ok)
            .OrderByDescending(s => s.SlotFolder == "exitsave_1")
            .ThenByDescending(s => s.SavedAtUtc)
            .First().SlotFolder;

    private static readonly string[] BagJunk =
        ["o_inv_cheese", "o_inv_goatcheese_rot", "o_inv_antitoxin_empty", "o_inv_potion04_empty", "o_inv_morion"];

    public static IReadOnlyList<ExperimentResult> Build(string root, GameServices game, BackupService backups, IGameProcessProbe probe)
    {
        var batch = Batch.Start(root, backups, probe);
        batch.Make("Osbrook", "A0: untouched copy; should load exactly like Velmir", _ => { });
        batch.Make("Brynn", "A1: STR 18, stat points 3, ability points 4, level 6, XP 2000, HP 999, Sanity 150, Hunger 0, Osbrook rep 9000, gold 99,999",
            s => Brynn(s, game));
        batch.Make("Mannshire", "A2: salve 1 charge, wineskin 2, helmet 50/105, junk deleted, a salve duplicated, Noble Longsword + Copper Ring + antitoxin + Pyromancy Treatise I added, probe salves in rows 5-9",
            s => Mannshire(s, game));
        batch.Make("Denbrie", "A3: 2 ability points; Hooking Chop unlearned; Killing Swing learned directly; Fatal Strike unlocked only",
            Denbrie);
        var closed = ClosedTreeTreatises(root, game);
        batch.Make("RottenWillow", $"A4: vol I treatise for each closed tree ({closed.Count}): read all, quit, then screenshot every skill tree",
            s => FillBagWith(s, game, closed));
        batch.Make("Brynn_NW", "A5: Pyromancy Treatise I-IV: read I, quit; reload, read II, quit; then III, then IV",
            s => FillBagWith(s, game, ["o_inv_treatise_pyro1", "o_inv_treatise_pyro2", "o_inv_treatise_pyro3", "o_inv_treatise_pyro4"]));
        return batch.Results;
    }

    /// <summary>
    /// Test characters for in-game session B (plan Tasks 12, 13 and 18): skill tree layouts, gold coin and ammo stacks,
    /// and a tree opened by the editor.
    /// </summary>
    public static IReadOnlyList<ExperimentResult> BuildSessionB(string root, GameServices game, BackupService backups, IGameProcessProbe probe)
    {
        var batch = Batch.Start(root, backups, probe);
        batch.Make("Brynn", "B1: every tree open and every skill learned, so each icon shows: screenshot every skill tree",
            s => LearnEverything(s, game));
        batch.Make("Denbrie", "B2: gold coin stacks of 100, 50 and 7 added to the bag; equipped bolts set to 5: check the stacks and the gold total",
            s => CoinsAndAmmo(s, game));
        batch.Make("Brynn_NW", "B3: Pyromancy opened by the editor (no book read); Treatises II-IV in the bag: check the tree is open, then read II, quit; reload, read III, quit; then IV",
            s => EditorOpensPyromancy(s, game));
        return batch.Results;
    }

    /// <summary>Test characters for in-game session C: gold written as coin stacks inside purses (after session B).</summary>
    public static IReadOnlyList<ExperimentResult> BuildSessionC(string root, GameServices game, BackupService backups, IGameProcessProbe probe)
    {
        var batch = Batch.Start(root, backups, probe);
        batch.Make("Osbrook", "C1: Velmir's purse rewritten as 1,234 gold in coins (13 stacks): check the total, open the purse, check it again",
            s => SetGold(s, game, 1234));
        batch.Make("Mannshire", "C2: 4,500 gold as three purses (2,000 + 2,000 + 500): check the total, open each purse, check it again",
            s => SetGold(s, game, 4500));
        return batch.Results;
    }

    /// <summary>
    /// Test character for in-game session D: with every treatise read, which skills does each skill need? The tiers'
    /// locks are out of the way, everything bought is unlearned and there are 100 ability points, so the owner learns
    /// skills in a given order and reports when each next one becomes learnable.
    /// </summary>
    public static IReadOnlyList<ExperimentResult> BuildSessionD(string root, GameServices game, BackupService backups, IGameProcessProbe probe)
    {
        var batch = Batch.Start(root, backups, probe);
        batch.Make("Denbrie",
            "D2 (every treatise read, 100 ability points, only starting skills learned): learn in the listed order and say when " +
            "each skill becomes learnable (see the checklist)",
            s => EveryBookRead(s, game));
        return batch.Results;
    }

    private static void EveryBookRead(SaveSession s, GameServices game)
    {
        var catalog = game.SkillsFor(s.Root);
        var skills = new SkillsEditor(s);
        skills.RespecAll();
        foreach (var tree in catalog.Trees.Where(t => t.TreatisePrefix is not null && catalog.Layout(t.Id) is not null))
        {
            var survivalStart = catalog.Layout(tree.Id)!.Skills.Where(p => catalog.VolumeOf(p.Key) is null).Select(p => p.Key).ToList();
            if (survivalStart.Count > 0)
                skills.Unlock(survivalStart); // rows open from the start (Survival's first)
            foreach (var volume in Enumerable.Range(1, catalog.Volumes(tree.Id)))
                skills.UnlockTree(catalog.TierSkills(tree.Id, volume), $"treatise_{tree.TreatisePrefix}{volume}");
        }
        new CharacterEditor(s).Set(CharacterField.AbilityPoints, 100);
    }

    private static void SetGold(SaveSession s, GameServices game, int amount) =>
        new InventoryEditor(s, game.Shapes).SetGold(amount,
            () => game.Catalog.CreateEntry("o_inv_moneybag", s.Root), () => game.Catalog.CreateEntry("o_inv_gold", s.Root));

    /// <summary>Clones Velmir's exit save once per experiment, applies it and saves the clone, re-signed.</summary>
    private sealed class Batch
    {
        private readonly string _root;
        private readonly BackupService _backups;
        private readonly IGameProcessProbe _probe;

        private Batch(string root, BackupService backups, IGameProcessProbe probe)
        {
            _root = root;
            _backups = backups;
            _probe = probe;
        }

        public List<ExperimentResult> Results { get; } = [];

        public static Batch Start(string root, BackupService backups, IGameProcessProbe probe) =>
            probe.IsGameRunning()
                ? throw new InvalidOperationException("Close Stoneshard before building experiment saves")
                : new Batch(root, backups, probe);

        public void Make(string label, string purpose, Action<SaveSession> apply)
        {
            var folder = TestCharacterCloner.Clone(_root, SourceCharacter, SourceSlot(_root), label, minutesLater: Results.Count + 1);
            var slot = new SaveLocator(_root).Scan().Single(c => c.Folder == folder).Slots.Single();
            var session = SaveSession.Open(slot);
            apply(session);
            if (session.ChangeCount > 0 && session.Save(_backups, _probe) is var result and not SaveResult.Saved)
                throw new InvalidOperationException($"Could not save experiment {label}: {result}");
            Results.Add(new ExperimentResult(folder, label, purpose));
        }
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
        // As tested in session A, above the game's maximum of 6000 that the editor now enforces.
        ((GmObject)((GmObject)((GmObject)s.Root["globalmapDataMap"]!)["locationsDataMap"]!)["32_10"]!).Set("reputation", GmNumber.From(9000));
        // As tested in session A: only the purse's cached total, which the game recounts from the coins when the purse
        // is opened (session B). The editor now writes coins (InventoryEditor.SetGold).
        var purse = new InventoryEditor(s, game.Shapes).Read().First(i => i.Name == "o_inv_moneybag").Index;
        ((GmObject)((GmArray)((GmArray)s.Root["inventoryDataList"]!).Items[purse]).Items[1]).Set("Stack", GmNumber.From(99_999));
        s.MarkChanged();
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

    /// <summary>Opens every tree with unlock data the way reading Volume I does, then learns every skill for free.</summary>
    private static void LearnEverything(SaveSession s, GameServices game)
    {
        var catalog = game.SkillsFor(s.Root);
        var skills = new SkillsEditor(s);
        foreach (var tree in catalog.Trees.Where(t => t.TreatisePrefix is not null && catalog.UnlockSet(t.Id).Count > 0))
            skills.UnlockTree(catalog.UnlockSet(tree.Id), $"treatise_{tree.TreatisePrefix}1");
        var learned = skills.Read().Where(x => x.Learned).Select(x => x.Object).ToHashSet();
        foreach (var obj in catalog.Trees.SelectMany(t => t.Skills).Select(k => k.Object).Where(o => !learned.Contains(o)))
            skills.Learn(obj, spendPoint: false);
    }

    private static void CoinsAndAmmo(SaveSession s, GameServices game)
    {
        var inv = new InventoryEditor(s, game.Shapes);
        inv.Add(game.Catalog.CreateEntry("o_inv_gold", s.Root));
        inv.SetCount(inv.Add(game.Catalog.CreateEntry("o_inv_gold", s.Root)), 50);
        inv.SetCount(inv.Add(game.Catalog.CreateEntry("o_inv_gold", s.Root)), 7);
        inv.SetCount(inv.Read().Single(i => i.Name == "o_inv_leafshaped_bolts").Index, 5);
    }

    private static void EditorOpensPyromancy(SaveSession s, GameServices game)
    {
        var catalog = game.SkillsFor(s.Root);
        var pyromancy = catalog.Trees.Single(t => t.TreatisePrefix == "pyro");
        new SkillsEditor(s).UnlockTree(catalog.UnlockSet(pyromancy.Id), "treatise_pyro1");
        FillBagWith(s, game, ["o_inv_treatise_pyro2", "o_inv_treatise_pyro3", "o_inv_treatise_pyro4"]);
    }

    /// <summary>Volume I of every tree that is still closed on Velmir: nothing learned and nothing unlocked.</summary>
    private static List<string> ClosedTreeTreatises(string root, GameServices game)
    {
        var file = Path.Combine(root, SourceCharacter, SourceSlot(root), "data.sav");
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

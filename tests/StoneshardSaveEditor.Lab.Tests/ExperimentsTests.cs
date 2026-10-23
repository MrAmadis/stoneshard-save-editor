using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;
using StoneshardSaveEditor.Lab;

namespace StoneshardSaveEditor.Tests;

public class ExperimentsTests
{
    private sealed class FakeProbe(bool running) : IGameProcessProbe
    {
        public bool IsGameRunning() => running;
    }

    private static readonly Lazy<GameServices> Game = new(() => GameServices.Load(Fixtures.GameDir));

    private static GmObject Load(string root, string character, string slot = "save_1")
    {
        var file = Path.Combine(root, character, slot, "data.sav");
        var decoded = SaveCodec.Decode(File.ReadAllBytes(file));
        Assert.True(SaveCodec.HashMatches(decoded, Salt.For(SaveFileKind.SlotFile, character, slot)), $"{character}/{slot} is not validly signed");
        return (GmObject)GmJson.Parse(decoded.Json);
    }

    private static IEnumerable<(string Name, int Cell)> BagItems(GmObject root) =>
        ((GmArray)root["inventoryDataList"]!).Items.Cast<GmArray>()
        .Where(e => ((GmString)e.Items[9]).Value == "N/A")
        .Select(e => (((GmString)e.Items[0]).Value, (int)((GmNumber)e.Items[3]).Value));

    [Fact]
    public void Experiments_build_expected_characters()
    {
        _ = Fixtures.GameDir;
        var root = Fixtures.CopyRealSavesToTemp();
        var realBefore = Directory.GetFiles(Path.Combine(root, "character_2"), "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.ReadAllBytes);
        var backups = new BackupService(Path.Combine(Path.GetDirectoryName(root)!, "backups"));

        var made = Experiments.Build(root, Game.Value, backups, new FakeProbe(false));

        Assert.Equal(new[] { "Osbrook", "Brynn", "Mannshire", "Denbrie", "RottenWillow", "Brynn_NW" }, made.Select(m => m.Label));
        Assert.Equal(new[] { "character_3", "character_4", "character_5", "character_6", "character_7", "character_8" }, made.Select(m => m.Folder));
        foreach (var (file, bytes) in realBefore)
            Assert.Equal(bytes, File.ReadAllBytes(file));

        var brynn = (GmObject)Load(root, "character_4")["characterDataMap"]!;
        Assert.Equal(("18.0", "3.0", "4.0", "150.0"), (((GmNumber)brynn["STR"]!).Raw, ((GmNumber)brynn["AP"]!).Raw, ((GmNumber)brynn["SP"]!).Raw, ((GmNumber)brynn["Sanity"]!).Raw));

        var mannshire = BagItems(Load(root, "character_5")).ToList();
        Assert.Equal(new[] { 50, 60, 70, 80, 90 }, mannshire.Where(i => i.Cell >= 50).Select(i => i.Cell).Order());
        Assert.Contains(mannshire, i => i.Name == "Noble Longsword");
        Assert.Contains(mannshire, i => i.Name == "o_inv_treatise_pyro1");
        Assert.DoesNotContain(mannshire, i => i.Name == "o_inv_cheese");

        var denbrie = SkillsEditor.ReadFrom(Load(root, "character_6"));
        Assert.False(denbrie.Single(s => s.Object == "o_skill_wide_cut_ico").Learned);
        Assert.True(denbrie.Single(s => s.Object == "o_skill_killing_swing_ico").Learned);
        Assert.False(denbrie.Single(s => s.Object == "o_pass_skill_fatal_hit").Locked);

        var books = BagItems(Load(root, "character_7")).Select(i => i.Name).ToList();
        Assert.All(books, b => Assert.Matches("^o_inv_treatise_[a-z]+1$", b));
        Assert.Contains("o_inv_treatise_pyro1", books);
        Assert.DoesNotContain("o_inv_treatise_greataxes1", books); // Greataxes is already open on Velmir

        Assert.Equal(new[] { "o_inv_treatise_pyro1", "o_inv_treatise_pyro2", "o_inv_treatise_pyro3", "o_inv_treatise_pyro4" },
            BagItems(Load(root, "character_8")).Select(i => i.Name).Order());
    }

    [Fact]
    public void Session_B_builds_tree_screenshot_coin_and_unlock_characters()
    {
        _ = Fixtures.GameDir;
        var root = Fixtures.CopyRealSavesToTemp();
        var backups = new BackupService(Path.Combine(Path.GetDirectoryName(root)!, "backups"));

        var made = Experiments.BuildSessionB(root, Game.Value, backups, new FakeProbe(false));

        Assert.Equal(new[] { "Brynn", "Denbrie", "Brynn_NW" }, made.Select(m => m.Label));

        var all = Load(root, made[0].Folder);
        var learned = SkillsEditor.ReadFrom(all).ToDictionary(s => s.Object, s => s.Learned);
        Assert.All(Game.Value.SkillsFor(all).Trees.SelectMany(t => t.Skills), k => Assert.True(learned[k.Object], k.Object));
        Assert.Contains("treatise_pyro1", Books(all));

        var bag = ((GmArray)Load(root, made[1].Folder)["inventoryDataList"]!).Items.Cast<GmArray>().ToList();
        double Count(GmArray e) => ((GmNumber)e.Items[6]).Value;
        Assert.Equal(new[] { 7.0, 50, 100 }, bag.Where(e => ((GmString)e.Items[0]).Value == "o_inv_gold").Select(Count).Order());
        Assert.Equal(5, Count(bag.Single(e => ((GmString)e.Items[0]).Value == "o_inv_leafshaped_bolts")));

        var unlock = Load(root, made[2].Folder);
        var state = SkillsEditor.ReadFrom(unlock).ToDictionary(s => s.Object);
        var catalog = Game.Value.SkillsFor(unlock);
        var pyro = catalog.UnlockSet(catalog.Trees.Single(t => t.TreatisePrefix == "pyro").Id);
        Assert.NotEmpty(pyro);
        Assert.All(pyro, k => Assert.Equal((false, false), (state[k].Learned, state[k].Locked)));
        Assert.Contains("treatise_pyro1", Books(unlock));
        Assert.Equal(new[] { "o_inv_treatise_pyro2", "o_inv_treatise_pyro3", "o_inv_treatise_pyro4" },
            BagItems(unlock).Select(i => i.Name).Order());
    }

    [Fact]
    public void Session_C_builds_gold_as_coins_characters()
    {
        _ = Fixtures.GameDir;
        var root = Fixtures.CopyRealSavesToTemp();
        var backups = new BackupService(Path.Combine(Path.GetDirectoryName(root)!, "backups"));

        var made = Experiments.BuildSessionC(root, Game.Value, backups, new FakeProbe(false));

        Assert.Equal(new[] { "Osbrook", "Mannshire" }, made.Select(m => m.Label));
        Assert.Equal(new[] { 1234.0 }, Purses(Load(root, made[0].Folder)).Select(p => p.Coins));
        Assert.Equal(new[] { 2000.0, 2000, 500 }, Purses(Load(root, made[1].Folder)).Select(p => p.Coins));
        Assert.All(Purses(Load(root, made[1].Folder)), p => Assert.Equal(p.Coins, p.Cached));
    }

    private static GmArray Named(GmObject save, string name) =>
        ((GmArray)save["inventoryDataList"]!).Items.Cast<GmArray>().Single(e => ((GmString)e.Items[0]).Value == name);

    private static string SlotOf(GmArray entry) => ((GmString)entry.Items[9]).Value;

    private static double Field(GmArray entry, int field) => ((GmNumber)entry.Items[field]).Value;

    [Fact]
    public void Session_G_builds_the_moved_gear_characters()
    {
        _ = Fixtures.GameDir;
        var root = Fixtures.CopyRealSavesToTemp();
        var backups = new BackupService(Path.Combine(Path.GetDirectoryName(root)!, "backups"));

        var made = Experiments.BuildSessionG(root, Game.Value, backups, new FakeProbe(false));

        Assert.Equal(new[] { "Osbrook", "Mannshire" }, made.Select(m => m.Label));
        var g1 = Load(root, made[0].Folder);
        var hood = Named(g1, "Hood");
        Assert.Equal("o_inv_head", SlotOf(hood));
        Assert.Equal(-4, Field(hood, 2));
        Assert.True(hood.Items[7] is GmBool { Value: true });
        Assert.Equal("N/A", SlotOf(Named(g1, "Flat Kettle Helmet")));
        Assert.Equal("o_inv_ring_1", SlotOf(Named(g1, "Antique Copper Ring")));
        Assert.Equal("o_inv_ring_2", SlotOf(Named(g1, "Velmir Ring")));
        var crossbow = Named(g1, "Guard Crossbow");
        Assert.Equal(("o_inv_right_hand", 0.0), (SlotOf(crossbow), Field(crossbow, 8)));
        var bolts = Named(g1, "o_inv_leafshaped_bolts");
        Assert.Equal(("o_inv_left_hand", 0.0, 19.0), (SlotOf(bolts), Field(bolts, 8), Field(bolts, 6)));
        var voulge = Named(g1, "Voulge");
        Assert.Equal(("o_inv_right_hand", 1.0, 0.0), (SlotOf(voulge), Field(voulge, 8), Field(voulge, 7)));

        var g2 = Load(root, made[1].Folder);
        Assert.All(new[] { "Makeshift Sling", "o_inv_ammo_bullet", "Lute", "Pickaxe", "Chain", "Sickle", "Peasant Bow", "o_inv_arrows", "o_inv_torch" },
            name => Assert.Equal("N/A", SlotOf(Named(g2, name))));
    }

    [Fact]
    public void Session_G_works_when_the_source_bag_is_full()
    {
        // The live Velmir carries more than the fixture; the first live run found no room to move the torch.
        _ = Fixtures.GameDir;
        var root = Fixtures.CopyRealSavesToTemp();
        var backups = new BackupService(Path.Combine(Path.GetDirectoryName(root)!, "backups"));
        var slot = new SaveLocator(root).Scan().Single(c => c.Folder == "character_2").Slots.Single(s => s.SlotFolder == "exitsave_1");
        var source = SaveSession.Open(slot);
        var inv = new InventoryEditor(source, Game.Value.Shapes);
        var torch = inv.Read().First(i => i.Name == "o_inv_torch").Index;
        for (var i = 0; i < 50 && inv.FirstFit(1, 3) is not null; i++)
            inv.Duplicate(torch);
        Assert.Null(inv.FirstFit(1, 3));
        Assert.Equal(SaveResult.Saved, source.Save(backups, new FakeProbe(false)));

        var made = Experiments.BuildSessionG(root, Game.Value, backups, new FakeProbe(false));

        Assert.Equal("o_inv_head", SlotOf(Named(Load(root, made[0].Folder), "Hood")));
    }

    [Fact]
    public void Session_D_builds_the_rules_check_character()
    {
        // Every treatise read, so the lock flags are out of the way and only the lines decide what can be learned.
        _ = Fixtures.GameDir;
        var root = Fixtures.CopyRealSavesToTemp();
        var backups = new BackupService(Path.Combine(Path.GetDirectoryName(root)!, "backups"));

        var made = Experiments.BuildSessionD(root, Game.Value, backups, new FakeProbe(false)).Single();

        AssertEveryBookReadAndNothingLearned(Load(root, made.Folder));
    }

    private static void AssertEveryBookReadAndNothingLearned(GmObject save)
    {
        var state = SkillsEditor.ReadFrom(save).ToDictionary(s => s.Object);
        var catalog = Game.Value.SkillsFor(save);
        var inLayouts = catalog.Trees.Where(t => catalog.Layout(t.Id) is not null).SelectMany(t => t.Skills).ToList();
        Assert.All(inLayouts, s => Assert.False(state[s.Object].Locked, s.Object));
        Assert.Equal(SkillsEditor.Innate.Order(), state.Values.Where(s => s.Learned).Select(s => s.Object).Order());
        Assert.Contains("treatise_shield2", ((GmArray)((GmObject)save["characterDataMap"]!)["Books_Read"]!).Items.OfType<GmString>().Select(b => b.Value));
        Assert.Equal(100, ((GmObject)save["characterDataMap"]!)["SP"]!.AsDouble());
    }

    [Fact]
    public void Reset_skills_gives_every_save_of_a_test_character_session_Ds_start()
    {
        // The owner played character_5 and wants it back to nothing learned, with every book still read.
        _ = Fixtures.GameDir;
        var root = Fixtures.CopyRealSavesToTemp();
        var backups = new BackupService(Path.Combine(Path.GetDirectoryName(root)!, "backups"));
        var made = TestCharacterCloner.Clone(root, "character_2", "exitsave_1");
        TestCharacterCloner.KeepAsSave(root, made, "save_1");

        var reset = Experiments.ResetSkills(root, made, Game.Value, backups, new FakeProbe(false));

        Assert.Equal(["save_1", "save_2"], reset.Order());
        Assert.All(reset, slot => AssertEveryBookReadAndNothingLearned(Load(root, made, slot)));
    }

    [Fact]
    public void Reset_skills_never_writes_the_owners_characters()
    {
        var root = Fixtures.CopyRealSavesToTemp();
        var before = File.ReadAllBytes(Path.Combine(root, "character_2", "exitsave_1", "data.sav"));

        Assert.Throws<InvalidOperationException>(() =>
            Experiments.ResetSkills(root, "character_2", Game.Value, new BackupService(Path.GetTempPath()), new FakeProbe(false)));

        Assert.Equal(before, File.ReadAllBytes(Path.Combine(root, "character_2", "exitsave_1", "data.sav")));
    }

    [Fact]
    public void Experiments_copy_Velmirs_newest_save_when_the_exit_save_is_gone()
    {
        // Loading an exit save in the game consumes it (sessions A-C); the owner's Velmir has none now.
        _ = Fixtures.GameDir;
        var root = Fixtures.CopyRealSavesToTemp();
        Directory.Delete(Path.Combine(root, "character_2", "exitsave_1"), recursive: true);
        var backups = new BackupService(Path.Combine(Path.GetDirectoryName(root)!, "backups"));

        var made = Experiments.BuildSessionD(root, Game.Value, backups, new FakeProbe(false)).Single();

        var save2 = (GmObject)GmJson.Parse(SaveCodec.Decode(File.ReadAllBytes(Path.Combine(root, "character_2", "save_2", "data.sav"))).Json);
        var clone = Load(root, made.Folder);
        Assert.Equal(((GmObject)save2["characterDataMap"]!)["XP"]!.AsDouble(), ((GmObject)clone["characterDataMap"]!)["XP"]!.AsDouble());
    }

    private static IEnumerable<(double Coins, double Cached)> Purses(GmObject root) =>
        ((GmArray)root["inventoryDataList"]!).Items.Cast<GmArray>()
        .Where(e => ((GmString)e.Items[0]).Value == "o_inv_moneybag")
        .Select(e => (GmObject)e.Items[1])
        .Select(p => (((GmArray)p["lootList"]!).Items.Cast<GmArray>().Sum(c => ((GmNumber)c.Items[6]).Value), ((GmNumber)p["Stack"]!).Value));

    private static IEnumerable<string> Books(GmObject root) =>
        ((GmArray)((GmObject)root["characterDataMap"]!)["Books_Read"]!).Items.OfType<GmString>().Select(b => b.Value);

    [Fact]
    public void Experiments_refuse_while_the_game_runs()
    {
        _ = Fixtures.GameDir;
        var root = Fixtures.CopyRealSavesToTemp();
        Assert.Throws<InvalidOperationException>(() =>
            Experiments.Build(root, Game.Value, new BackupService(Path.GetTempPath()), new FakeProbe(true)));
        Assert.False(Directory.Exists(Path.Combine(root, "character_3")));
    }

    [Fact]
    public void Unlock_extractor_groups_new_unlocks_by_treatise()
    {
        var before = (GmObject)GmJson.Parse("{ \"skillsDataMap\": { \"skillsAllDataList\": [ \"a\", 0.0, 0.0, 1.0, 0.0, \"b\", 0.0, 0.0, true, 0.0, \"c\", 0.0, 0.0, 1.0, 0.0, \"d\", 0.0, 0.0, false, 0.0 ] } }"u8);
        var after = (GmObject)GmJson.Parse("{ \"skillsDataMap\": { \"skillsAllDataList\": [ \"a\", 0.0, 0.0, false, 0.0, \"b\", 0.0, 0.0, 0.0, 0.0, \"c\", 0.0, 0.0, 1.0, 0.0, \"d\", 0.0, 0.0, false, 0.0 ] } }"u8);

        var unlocked = UnlockExtractor.NewlyUnlocked(before, after);
        Assert.Equal(new[] { "a", "b" }, unlocked);

        var grouped = UnlockExtractor.GroupByTreatise(unlocked, s => s == "a" ? "pyro" : "geo", volume: 1);
        Assert.Equal(new[] { "geo1", "pyro1" }, grouped.Keys.Order());
        Assert.Equal(new[] { "a" }, grouped["pyro1"]);
    }

    [Fact]
    public void Unlock_extractor_credits_only_treatises_that_were_read()
    {
        var before = (GmObject)GmJson.Parse("{ \"characterDataMap\": { \"Books_Read\": [ \"map_osbrook\" ] } }"u8);
        var after = (GmObject)GmJson.Parse("{ \"characterDataMap\": { \"Books_Read\": [ \"map_osbrook\", \"treatise_pyro1\", \"lorebook_x\" ] } }"u8);

        Assert.Equal(new[] { "pyro1" }, UnlockExtractor.NewlyReadTreatises(before, after));

        var grouped = UnlockExtractor.GroupByTreatise(["a", "b"], s => s == "a" ? "pyro" : "survival", volume: 1, onlyRead: ["pyro1"]);
        Assert.Equal(new[] { "pyro1" }, grouped.Keys);
    }

    [Fact]
    public void Unlock_extractor_merges_into_json_file()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory().FullName, "tree-unlocks.json");
        File.WriteAllText(file, "{ \"pyro1\": [\"a\"] }");

        UnlockExtractor.Merge(file, new Dictionary<string, string[]> { ["pyro1"] = ["b", "a"], ["geo1"] = ["c"] });

        var merged = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(file))!;
        Assert.Equal(new[] { "a", "b" }, merged["pyro1"]);
        Assert.Equal(new[] { "c" }, merged["geo1"]);
    }

    [Fact]
    public void Snapshots_capture_test_characters_only()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var outDir = Path.Combine(root, "out");
        foreach (var c in new[] { "character_2", "character_5" })
        {
            Directory.CreateDirectory(Path.Combine(root, c, "exitsave_1"));
            File.WriteAllText(Path.Combine(root, c, "exitsave_1", "data.sav"), c);
        }

        Assert.Null(SnapshotStore.Capture(root, Path.Combine(root, "character_2", "exitsave_1", "data.sav"), outDir));
        var copy = SnapshotStore.Capture(root, Path.Combine(root, "character_5", "exitsave_1", "data.sav"), outDir);

        Assert.NotNull(copy);
        Assert.StartsWith(Path.Combine(outDir, "character_5"), copy);
        Assert.EndsWith("-data.sav", copy);
        Assert.Equal("character_5", File.ReadAllText(copy!));
    }
}

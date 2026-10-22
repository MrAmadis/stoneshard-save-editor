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

    private static GmObject Load(string root, string character)
    {
        var file = Path.Combine(root, character, "exitsave_1", "data.sav");
        var decoded = SaveCodec.Decode(File.ReadAllBytes(file));
        Assert.True(SaveCodec.HashMatches(decoded, Salt.For(SaveFileKind.SlotFile, character, "exitsave_1")), $"{character} is not validly signed");
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

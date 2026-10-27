using System.Text.Json;
using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;
using StoneshardSaveEditor.UI.Services;

namespace StoneshardSaveEditor.Tests;

public class EditorServiceTests
{
    public sealed class FakeProbe : IGameProcessProbe
    {
        public bool Running { get; set; }
        public bool IsGameRunning() => Running;
    }

    public sealed record Harness(EditorService Editor, string Root, FakeProbe Probe, string SettingsPath);

    public static Harness Start(string? savesRoot = null, string? gameDir = null)
    {
        var root = savesRoot ?? Fixtures.CopyRealSavesToTemp();
        var work = Directory.CreateTempSubdirectory().FullName;
        var settings = Path.Combine(work, "settings.json");
        if (gameDir is not null)
            File.WriteAllText(settings, JsonSerializer.Serialize(new { gameDir }));
        var probe = new FakeProbe();
        var options = new EditorOptions(root, Path.Combine(work, "backups"), Path.Combine(work, "cache"), settings, probe, null);
        var editor = new EditorService(options, new GameDataProvider(() => Settings.Load(settings).GameDir, options.CacheRoot));
        return new Harness(editor, root, probe, settings);
    }

    private static GmObject LoadFromDisk(Harness h)
    {
        var file = Path.Combine(h.Root, "character_2", "exitsave_1", "data.sav");
        return (GmObject)GmJson.Parse(SaveCodec.Decode(File.ReadAllBytes(file)).Json);
    }

    [Fact]
    public void Characters_list_velmir_with_named_slots()
    {
        var characters = Start().Editor.Characters();

        Assert.Equal(2, characters.Count);
        var velmir = characters[1];
        Assert.Equal(("Velmir", "Revenger", 5, "s_Velmir"), (velmir.Name, velmir.ClassName, velmir.Level, velmir.AvatarSprite));
        Assert.Equal(6, velmir.Slots.Count);
        Assert.Equal(("exitsave_1", "Exit save", "Caravan Camp", SlotStatus.Ok),
            (velmir.Slots[0].Folder, velmir.Slots[0].DisplayName, velmir.Slots[0].Location, velmir.Slots[0].Status));
        Assert.Contains(velmir.Slots, s => s.DisplayName == "Autosave 3");
        Assert.Contains(velmir.Slots, s => s.DisplayName == "Save 2");
    }

    [Fact]
    public void Open_edit_save_round_trip()
    {
        var h = Start();
        var session = h.Editor.Open("character_2", "exitsave_1");
        Assert.Equal(13, session.Character[CharacterField.Strength]);
        Assert.Equal(0, session.Gold); // real coins; the purse's cached 2864 is stale (session B)
        Assert.Equal("The purse says 2864 gold but holds 0 in coins. The game shows 0 once the purse is opened; set the gold here to put real coins in it.",
            session.GoldNote);
        Assert.Equal(5, session.BagRows);
        Assert.Equal(33, session.Items.Count);
        Assert.Equal(5, session.Reputation.Count);
        Assert.Equal("Velmir", session.Name);

        Assert.Equal(1, h.Editor.SetCharacter(CharacterField.Strength, 18).ChangeCount);
        Assert.Equal(SaveResult.Saved, h.Editor.Save());

        var reopened = h.Editor.Open("character_2", "exitsave_1");
        Assert.Equal(18, reopened.Character[CharacterField.Strength]);
        Assert.Equal(0, reopened.ChangeCount);
    }

    [Fact]
    public void Reload_discards_unsaved_edits()
    {
        var h = Start();
        h.Editor.Open("character_2", "exitsave_1");
        h.Editor.SetReputation("32_10", 5500);

        var reloaded = h.Editor.Reload();

        Assert.Equal(4258, reloaded.Reputation.Single(r => r.LocationKey == "32_10").Value);
        Assert.Equal(0, reloaded.ChangeCount);
    }

    [Fact]
    public void Edit_rejections_carry_the_message()
    {
        var h = Start();
        h.Editor.Open("character_2", "exitsave_1");

        var e = Assert.Throws<EditRejectedException>(() => h.Editor.SetCharacter(CharacterField.Sanity, 150));
        Assert.Contains("Sanity", e.Message);
        Assert.Throws<EditRejectedException>(() => h.Editor.SetCharges(999, 2));
        Assert.Equal(0, h.Editor.Current.ChangeCount);
    }

    [Fact]
    public void Using_the_session_before_opening_fails()
    {
        var h = Start();
        Assert.False(h.Editor.IsOpen);
        Assert.Throws<InvalidOperationException>(() => h.Editor.Current);
        Assert.Throws<InvalidOperationException>(() => h.Editor.SetCharacter(CharacterField.Strength, 18));
    }

    [Fact]
    public void Close_returns_to_no_session()
    {
        var h = Start();
        h.Editor.Open("character_2", "exitsave_1");
        var fired = 0;
        h.Editor.Changed += () => fired++;

        h.Editor.Close();

        Assert.False(h.Editor.IsOpen);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Changed_event_fires_on_edits()
    {
        var h = Start();
        h.Editor.Open("character_2", "exitsave_1");
        var fired = 0;
        h.Editor.Changed += () => fired++;

        h.Editor.SetCharacter(CharacterField.Strength, 18);

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Save_reports_gameRunning()
    {
        var h = Start();
        h.Editor.Open("character_2", "exitsave_1");
        h.Editor.SetCharacter(CharacterField.Strength, 18);
        h.Probe.Running = true;

        Assert.True(h.Editor.GameRunning());
        Assert.Equal(SaveResult.GameRunning, h.Editor.Save());
        Assert.Equal(1, h.Editor.Current.ChangeCount);
    }

    [Fact]
    public void Save_reports_changedOnDisk()
    {
        var h = Start();
        h.Editor.Open("character_2", "exitsave_1");
        h.Editor.SetCharacter(CharacterField.Strength, 18);
        var file = Path.Combine(h.Root, "character_2", "exitsave_1", "data.sav");
        var json = SaveCodec.Decode(File.ReadAllBytes(file)).Json.Concat(" "u8.ToArray()).ToArray();
        File.WriteAllBytes(file, SaveCodec.Encode(json, Salt.For(SaveFileKind.SlotFile, "character_2", "exitsave_1")));

        Assert.Equal(SaveResult.ChangedOnDisk, h.Editor.Save());
    }

    [Fact]
    public void Read_only_session_rejects_edits()
    {
        var h = Start();
        var mapFile = Path.Combine(h.Root, "character_2", "save_1", "save.map");
        var map = (GmObject)GmJson.Parse(SaveCodec.Decode(File.ReadAllBytes(mapFile)).Json);
        map.Set("wipeVersion", GmString.From("0.10"));
        File.WriteAllBytes(mapFile, SaveCodec.Encode(GmJson.Write(map), Salt.For(SaveFileKind.SlotFile, "character_2", "save_1")));

        var session = h.Editor.Open("character_2", "save_1");

        Assert.True(session.ReadOnly);
        var e = Assert.Throws<EditRejectedException>(() => h.Editor.SetCharacter(CharacterField.Strength, 18));
        Assert.StartsWith("This save is read-only", e.Message);
    }

    [Fact]
    public void Backups_are_listed_and_restorable()
    {
        var h = Start();
        h.Editor.Open("character_2", "exitsave_1");
        h.Editor.SetCharacter(CharacterField.Strength, 18);
        h.Editor.Save();

        var backup = Assert.Single(h.Editor.Backups());
        var restored = h.Editor.Restore(backup.Path);

        Assert.Equal(13, restored.Character[CharacterField.Strength]);
        Assert.Throws<EditRejectedException>(() => h.Editor.Restore(@"C:\Windows\win.ini"));
    }

    [Fact]
    public void Game_data_powers_trees_catalog_and_unlock()
    {
        var h = Start(gameDir: Fixtures.GameDir);
        var session = h.Editor.Open("character_2", "exitsave_1");

        Assert.True(session.GameFound);
        Assert.Equal(22, session.Trees.Count);
        var greataxes = session.Trees.Single(t => t.Id == "2haxe");
        Assert.Equal(("Greataxes", true, 3), (greataxes.DisplayName, greataxes.Open, greataxes.LearnedCount));
        Assert.Equal("Hooking Chop", greataxes.Skills.Single(s => s.Object == "o_skill_wide_cut_ico").DisplayName);
        var pyromancy = session.Trees.Single(t => t.Id == "pyromancy");
        Assert.Equal((false, true), (pyromancy.Open, pyromancy.CanUnlock));
        Assert.True(session.RulesAvailable);
        Assert.Equal("Caravan Camp", session.Location);
        Assert.Contains(session.Reputation, r => r.Name == "Rotten Willow");
        Assert.Equal("s_inv_moneybag", session.Items.Single(i => i.Name == "o_inv_moneybag").Sprite);

        Assert.True(h.Editor.Catalog().Count > 1000);
        Assert.Contains(h.Editor.AddItem("o_inv_treatise_pyro1").Items, i => i.Name == "o_inv_treatise_pyro1");

        var opened = h.Editor.UnlockTree("pyromancy").Trees.Single(t => t.Id == "pyromancy");
        Assert.True(opened.Open);
        Assert.Equal(4, opened.Skills.Count(s => !s.Locked)); // Volume I's first tier only
        Assert.Throws<EditRejectedException>(() => h.Editor.UnlockTree("basic"));

        Assert.Equal(6, h.Editor.RespecAll().Character[CharacterField.AbilityPoints]);
        Assert.Equal(SaveResult.Saved, h.Editor.Save());
        var books = ((GmArray)((GmObject)LoadFromDisk(h)["characterDataMap"]!)["Books_Read"]!).Items.OfType<GmString>().Select(s => s.Value);
        Assert.Contains("treatise_pyro1", books);
    }

    [Fact]
    public void Missing_saves_root_is_reported()
    {
        var h = Start(savesRoot: @"C:\does\not\exist");
        Assert.False(h.Editor.Setup().SavesFound);
        Assert.Empty(h.Editor.Characters());
        Assert.Null(h.Editor.BrowseForGameDir());
    }

    [Fact]
    public void Unparseable_game_files_degrade_gracefully()
    {
        var junk = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(junk, "data.win"), "junk");
        File.WriteAllText(Path.Combine(junk, "StoneShard.exe"), "junk");
        var h = Start(gameDir: junk);

        var setup = h.Editor.Setup();
        Assert.False(setup.GameFound);
        Assert.False(string.IsNullOrEmpty(setup.GameError));

        var session = h.Editor.Open("character_2", "exitsave_1");
        Assert.False(session.GameFound);
        Assert.Empty(session.Trees);
        Assert.Equal("missing", session.Items[0].Sprite);
        Assert.Equal(1, h.Editor.SetCharacter(CharacterField.Strength, 18).ChangeCount);
        Assert.Throws<EditRejectedException>(() => h.Editor.Catalog());
        Assert.Throws<EditRejectedException>(() => h.Editor.DuplicateItem(0));
    }

    [Fact]
    public void Game_dir_is_validated_and_saved()
    {
        var h = Start();
        Assert.Throws<EditRejectedException>(() => h.Editor.SetGameDir(Directory.CreateTempSubdirectory().FullName));

        h.Editor.SetGameDir(Fixtures.GameDir);

        Assert.Equal(Fixtures.GameDir, Settings.Load(h.SettingsPath).GameDir);
        Assert.True(h.Editor.Setup().GameFound);
    }

    [Theory]
    [InlineData("exitsave_1", "Exit save")]
    [InlineData("save_2", "Save 2")]
    [InlineData("autosave_3", "Autosave 3")]
    public void Slot_names_are_pretty(string folder, string expected) =>
        Assert.Equal(expected, Names.SlotDisplayName(folder));

    [Theory]
    [InlineData("RottenWillow", "Rotten Willow")]
    [InlineData("Brynn_NW", "Brynn NW")]
    [InlineData("CaravanCamp", "Caravan Camp")]
    [InlineData("Osbrook", "Osbrook")]
    public void Keys_are_pretty_without_localization(string key, string expected) =>
        Assert.Equal(expected, Names.Pretty(key, null));

    [Theory]
    [InlineData("Voulge", "Voulge had no room and was removed")]
    [InlineData("Guard Crossbow|Leafshaped Bolts", "Guard Crossbow and Leafshaped Bolts had no room and were removed")]
    public void Removed_items_read_as_a_sentence(string names, string expected) =>
        Assert.Equal(expected, Names.NoRoom(names.Split('|')));

    [Theory]
    [InlineData("A", "A")]
    [InlineData("A|B", "A or B")]
    [InlineData("A+B+C", "A, B and C")]
    [InlineData("A|B+C|D", "A or B, and C or D")]
    [InlineData("A+B|C", "A, and B or C")]
    public void Conditions_read_as_a_sentence(string conditions, string expected) =>
        Assert.Equal(expected, Names.Conditions(conditions.Split('+').Select(c => c.Split('|')).ToList()));

    [Fact]
    public void Moving_an_item_updates_the_view()
    {
        var h = Start(gameDir: Fixtures.GameDir);
        h.Editor.Open("character_2", "exitsave_1");
        var helmet = h.Editor.Current.Items.Single(i => i.Name == "Flat Kettle Helmet");
        var bag = h.Editor.Current.Items.Where(i => !i.Equipped && i.X is not null).ToList();
        var spot = Enumerable.Range(0, InventoryEditor.BagRows - helmet.H + 1)
            .SelectMany(y => Enumerable.Range(0, InventoryEditor.Columns - helmet.W + 1).Select(x => (X: x, Y: y)))
            .First(p => !bag.Any(b => p.X < b.X + b.W && b.X < p.X + helmet.W && p.Y < b.Y + b.H && b.Y < p.Y + helmet.H));

        var result = h.Editor.MoveItem(helmet.Index, ItemPlace.InBag(spot.X, spot.Y));

        var moved = h.Editor.Current.Items[result.Index];
        Assert.Equal(("Flat Kettle Helmet", false, spot.X, spot.Y), (moved.Name, moved.Equipped, moved.X!.Value, moved.Y!.Value));
        Assert.Equal(1, h.Editor.Current.ChangeCount);
    }

    [Fact]
    public void Asking_whether_a_move_is_allowed_changes_nothing()
    {
        var h = Start(gameDir: Fixtures.GameDir);
        h.Editor.Open("character_2", "exitsave_1");
        var helmet = h.Editor.Current.Items.Single(i => i.Name == "Flat Kettle Helmet");

        Assert.Equal("That doesn't go there", h.Editor.MoveRefusal(helmet.Index, ItemPlace.Worn(GearRules.Feet)));
        Assert.Equal(0, h.Editor.Current.ChangeCount);
    }

    [Fact]
    public void Dropping_an_item_where_it_is_does_no_edit()
    {
        var h = Start(gameDir: Fixtures.GameDir);
        h.Editor.Open("character_2", "exitsave_1");
        var changed = 0;
        h.Editor.Changed += () => changed++;
        var helmet = h.Editor.Current.Items.Single(i => i.Name == "Flat Kettle Helmet");

        var result = h.Editor.MoveItem(helmet.Index, ItemPlace.Worn(GearRules.Head));

        Assert.Equal(helmet.Index, result.Index);
        Assert.Empty(result.Removed);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void In_a_read_only_save_nothing_can_be_moved()
    {
        var h = Start(gameDir: Fixtures.GameDir);
        var mapFile = Path.Combine(h.Root, "character_2", "save_1", "save.map");
        var map = (GmObject)GmJson.Parse(SaveCodec.Decode(File.ReadAllBytes(mapFile)).Json);
        map.Set("wipeVersion", GmString.From("0.10"));
        File.WriteAllBytes(mapFile, SaveCodec.Encode(GmJson.Write(map), Salt.For(SaveFileKind.SlotFile, "character_2", "save_1")));
        h.Editor.Open("character_2", "save_1");
        var head = h.Editor.Current.Items.First(i => i.Slot == GearRules.Head);
        var spot = h.Editor.Current.Items.Where(i => i.Equipped).First(i => i.Slot == GearRules.Ring1);

        Assert.StartsWith("This save is read-only", h.Editor.MoveRefusal(head.Index, ItemPlace.Worn(GearRules.Head)));
        Assert.StartsWith("This save is read-only", h.Editor.MoveRefusal(spot.Index, ItemPlace.Worn(GearRules.Ring2)));
    }
}


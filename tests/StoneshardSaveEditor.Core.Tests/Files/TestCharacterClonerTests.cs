using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Tests.Files;

public class TestCharacterClonerTests
{
    private static GmObject DecodeVerified(string file, string salt)
    {
        var decoded = SaveCodec.Decode(File.ReadAllBytes(file));
        Assert.True(SaveCodec.HashMatches(decoded, salt), $"bad checksum for {file}");
        return (GmObject)GmJson.Parse(decoded.Json);
    }

    [Fact]
    public void Clone_creates_next_character_with_resigned_files()
    {
        var root = Fixtures.CopyRealSavesToTemp();
        var sourceSlot = Path.Combine(root, "character_2", "exitsave_1");
        var sourceBytes = Directory.GetFiles(sourceSlot).ToDictionary(f => Path.GetFileName(f), File.ReadAllBytes);

        var created = TestCharacterCloner.Clone(root, "character_2", "exitsave_1", "Osbrook");

        Assert.Equal("character_3", created);
        DecodeVerified(Path.Combine(root, "character_3", "character.map"), "stOne!characters_v1!character_3!shArd");
        const string slotSalt = "stOne!characters_v1!character_3!save_1!shArd";
        var map = DecodeVerified(Path.Combine(root, "character_3", "save_1", "save.map"), slotSalt);
        Assert.Equal("Osbrook", ((GmString)map["locationTitleKey"]!).Value);
        DecodeVerified(Path.Combine(root, "character_3", "save_1", "data.sav"), slotSalt);
        Assert.Equal(
            SaveCodec.Decode(sourceBytes["data.sav"]).Json,
            SaveCodec.Decode(File.ReadAllBytes(Path.Combine(root, "character_3", "save_1", "data.sav"))).Json);
        Assert.True(File.Exists(Path.Combine(root, "character_3", "save_1", "preview.png")));
        foreach (var (name, bytes) in sourceBytes)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(sourceSlot, name)));
        // A regular save, which the game keeps when it loads it (an exit save is consumed, sessions A-C).
        Assert.Equal(["save_1"], Directory.GetDirectories(Path.Combine(root, "character_3")).Select(Path.GetFileName));
    }

    [Fact]
    public void Keep_as_save_copies_a_test_characters_slot_to_its_next_regular_save()
    {
        var root = Fixtures.CopyRealSavesToTemp();
        var made = TestCharacterCloner.Clone(root, "character_2", "exitsave_1");
        // As the game leaves it after a quit: an exit save signed for its own folder.
        var exit = Directory.CreateDirectory(Path.Combine(root, made, "exitsave_1")).FullName;
        foreach (var file in Directory.GetFiles(Path.Combine(root, made, "save_1")))
            File.WriteAllBytes(Path.Combine(exit, Path.GetFileName(file)), Path.GetExtension(file) == ".png"
                ? File.ReadAllBytes(file)
                : SaveCodec.Encode(SaveCodec.Decode(File.ReadAllBytes(file)).Json, Salt.For(SaveFileKind.SlotFile, made, "exitsave_1")));
        Directory.Delete(Path.Combine(root, made, "save_1"), recursive: true);
        var played = File.ReadAllBytes(Path.Combine(exit, "data.sav"));

        Assert.Equal("save_1", TestCharacterCloner.KeepAsSave(root, made, "exitsave_1"));
        Assert.Equal("save_2", TestCharacterCloner.KeepAsSave(root, made, "exitsave_1"));

        const string salt = "stOne!characters_v1!character_3!save_2!shArd";
        DecodeVerified(Path.Combine(root, made, "save_2", "save.map"), salt);
        DecodeVerified(Path.Combine(root, made, "save_2", "data.sav"), salt);
        Assert.Equal(SaveCodec.Decode(played).Json, SaveCodec.Decode(File.ReadAllBytes(Path.Combine(root, made, "save_2", "data.sav"))).Json);
        Assert.True(File.Exists(Path.Combine(root, made, "save_2", "preview.png")));
        Assert.Equal(played, File.ReadAllBytes(Path.Combine(root, made, "exitsave_1", "data.sav")));
    }

    [Fact]
    public void Keep_as_save_never_writes_the_owners_characters()
    {
        var root = Fixtures.CopyRealSavesToTemp();

        Assert.Throws<InvalidOperationException>(() => TestCharacterCloner.KeepAsSave(root, "character_2", "exitsave_1"));

        Assert.False(Directory.Exists(Path.Combine(root, "character_2", "save_3")));
    }

    [Fact]
    public void Clone_reuses_the_lowest_free_number_above_the_owners_characters()
    {
        // The game lists character_1..character_10 only (session C), so test characters fill gaps instead of growing.
        var root = Fixtures.CopyRealSavesToTemp();
        foreach (var n in new[] { 3, 4, 11 })
            Directory.CreateDirectory(Path.Combine(root, $"character_{n}"));
        Directory.Delete(Path.Combine(root, "character_1"), recursive: true);

        Assert.Equal("character_5", TestCharacterCloner.Clone(root, "character_2", "exitsave_1"));
        Assert.Equal("character_6", TestCharacterCloner.Clone(root, "character_2", "exitsave_1"));
    }

    [Fact]
    public void Clone_refuses_when_every_slot_the_game_lists_is_used()
    {
        var root = Fixtures.CopyRealSavesToTemp();
        foreach (var n in Enumerable.Range(3, 8))
            Directory.CreateDirectory(Path.Combine(root, $"character_{n}"));

        var error = Assert.Throws<InvalidOperationException>(() => TestCharacterCloner.Clone(root, "character_2", "exitsave_1"));

        Assert.Contains("character_10", error.Message);
        Assert.False(Directory.Exists(Path.Combine(root, "character_11")));
    }

    [Fact]
    public void Clone_with_a_missing_source_creates_nothing()
    {
        var root = Fixtures.CopyRealSavesToTemp();
        Directory.Delete(Path.Combine(root, "character_2", "exitsave_1"), recursive: true);

        Assert.Throws<FileNotFoundException>(() => TestCharacterCloner.Clone(root, "character_2", "exitsave_1"));

        Assert.False(Directory.Exists(Path.Combine(root, "character_3")));
    }

    [Fact]
    public void Clone_without_label_keeps_location()
    {
        var root = Fixtures.CopyRealSavesToTemp();
        var source = DecodeVerified(Path.Combine(root, "character_2", "save_1", "save.map"), "stOne!characters_v1!character_2!save_1!shArd");
        TestCharacterCloner.Clone(root, "character_2", "save_1");
        var map = DecodeVerified(Path.Combine(root, "character_3", "save_1", "save.map"), "stOne!characters_v1!character_3!save_1!shArd");
        Assert.Equal("Brynn_NW", ((GmString)source["locationTitleKey"]!).Value);
        Assert.Equal("Brynn_NW", ((GmString)map["locationTitleKey"]!).Value);
    }

    [Fact]
    public void Clone_can_shift_the_save_time()
    {
        var root = Fixtures.CopyRealSavesToTemp();
        TestCharacterCloner.Clone(root, "character_2", "exitsave_1", minutesLater: 30);

        var original = DecodeVerified(Path.Combine(root, "character_2", "exitsave_1", "save.map"), "stOne!characters_v1!character_2!exitsave_1!shArd");
        var shifted = DecodeVerified(Path.Combine(root, "character_3", "save_1", "save.map"), "stOne!characters_v1!character_3!save_1!shArd");
        var characterMap = DecodeVerified(Path.Combine(root, "character_3", "character.map"), "stOne!characters_v1!character_3!shArd");
        var delta = ((GmNumber)shifted["dateTime"]!).Value - ((GmNumber)original["dateTime"]!).Value;
        Assert.Equal(30.0 / 1440, delta, 9);
        Assert.Equal(((GmNumber)shifted["dateTime"]!).Value, ((GmNumber)characterMap["dateTime"]!).Value, 9);
    }

    [Fact]
    public void PointContinue_rewrites_characters_map_after_backing_it_up()
    {
        var root = Fixtures.CopyRealSavesToTemp();
        var backupDir = Path.Combine(Path.GetDirectoryName(root)!, "backups");

        TestCharacterCloner.PointContinue(root, "character_1", "save_1", backupDir);

        var map = DecodeVerified(Path.Combine(root, "characters.map"), "stOne!characters_v1!shArd");
        Assert.Equal("character_1", ((GmString)map["lastCharacter"]!).Value);
        Assert.Equal("save_1", ((GmString)map["lastSave"]!).Value);
        var backup = Assert.Single(Directory.GetFiles(backupDir, "characters.map.*"));
        var old = DecodeVerified(backup, "stOne!characters_v1!shArd");
        Assert.Equal("character_2", ((GmString)old["lastCharacter"]!).Value);
    }
}

using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Tests.Files;

public class SaveLocatorTests
{
    [Fact]
    public void Scan_lists_both_characters_newest_slot_first()
    {
        var characters = new SaveLocator(Fixtures.CopyRealSavesToTemp()).Scan();

        Assert.Equal(new[] { "character_1", "character_2" }, characters.Select(c => c.Folder));
        var velmir = characters[1];
        Assert.Equal("Velmir", velmir.NameKey);
        Assert.Equal("Revenger", velmir.ClassKey);
        Assert.Equal(5, velmir.Level);
        Assert.Equal("s_Velmir", velmir.AvatarSprite);
        Assert.Equal(6, velmir.Slots.Count);
        Assert.Equal("exitsave_1", velmir.Slots[0].SlotFolder);
        Assert.Equal("CaravanCamp", velmir.Slots[0].LocationKey);
        Assert.All(velmir.Slots, s => Assert.Equal(SlotStatus.Ok, s.Status));
        Assert.True(velmir.Slots.Zip(velmir.Slots.Skip(1)).All(p => p.First.SavedAtUtc >= p.Second.SavedAtUtc));

        var jorgrim = characters[0];
        Assert.Equal("Jorgrim", jorgrim.NameKey);
        Assert.Equal(1, jorgrim.Level);
        Assert.Equal("Mill", jorgrim.Slots.Single(s => s.SlotFolder == "save_1").LocationKey);
    }

    [Fact]
    public void Scan_ignores_non_slot_folders()
    {
        var velmir = new SaveLocator(Fixtures.CopyRealSavesToTemp()).Scan().Single(c => c.Folder == "character_2");
        Assert.DoesNotContain(velmir.Slots, s => s.SlotFolder.Contains("backup"));
    }

    [Fact]
    public void Damaged_slot_is_reported_not_thrown()
    {
        var root = Fixtures.CopyRealSavesToTemp();
        File.WriteAllText(Path.Combine(root, "character_2", "save_1", "data.sav"), "junk");

        var slots = new SaveLocator(root).Scan().Single(c => c.Folder == "character_2").Slots;

        Assert.Equal(SlotStatus.Damaged, slots.Single(s => s.SlotFolder == "save_1").Status);
        Assert.All(slots.Where(s => s.SlotFolder != "save_1"), s => Assert.Equal(SlotStatus.Ok, s.Status));
    }

    [Fact]
    public void Hash_mismatch_is_damaged()
    {
        var root = Fixtures.CopyRealSavesToTemp();
        var file = Path.Combine(root, "character_2", "save_2", "data.sav");
        var decoded = SaveCodec.Decode(File.ReadAllBytes(file));
        File.WriteAllBytes(file, SaveCodec.Encode(decoded.Json, Salt.For(SaveFileKind.SlotFile, "character_2", "save_9")));

        var slot = new SaveLocator(root).Scan().Single(c => c.Folder == "character_2").Slots.Single(s => s.SlotFolder == "save_2");

        Assert.Equal(SlotStatus.Damaged, slot.Status);
        Assert.Contains("checksum", slot.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsupported_version_is_flagged()
    {
        var root = Fixtures.CopyRealSavesToTemp();
        var file = Path.Combine(root, "character_2", "save_1", "save.map");
        var map = (GmObject)GmJson.Parse(SaveCodec.Decode(File.ReadAllBytes(file)).Json);
        map.Set("wipeVersion", GmString.From("0.10"));
        File.WriteAllBytes(file, SaveCodec.Encode(GmJson.Write(map), Salt.For(SaveFileKind.SlotFile, "character_2", "save_1")));

        var slot = new SaveLocator(root).Scan().Single(c => c.Folder == "character_2").Slots.Single(s => s.SlotFolder == "save_1");

        Assert.Equal(SlotStatus.UnsupportedVersion, slot.Status);
    }

    [Fact]
    public void GameDate_converts_days_to_utc() =>
        Assert.InRange(GameDate.ToUtc(46316.836047453703940846025943756),
            new DateTime(2026, 10, 21, 20, 3, 54, DateTimeKind.Utc),
            new DateTime(2026, 10, 21, 20, 3, 55, DateTimeKind.Utc));

    [Fact]
    public void Missing_root_returns_empty_list() =>
        Assert.Empty(new SaveLocator(@"C:\does\not\exist").Scan());
}

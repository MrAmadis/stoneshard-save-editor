using System.IO.Compression;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Tests.Files;

public class SaveSessionTests
{
    private sealed class FakeProbe(bool running) : IGameProcessProbe
    {
        public bool IsGameRunning() => running;
    }

    private static readonly IGameProcessProbe NotRunning = new FakeProbe(false);

    private static (SlotInfo Slot, BackupService Backups, string BackupRoot) Setup(string slot = "exitsave_1")
    {
        var root = Fixtures.CopyRealSavesToTemp();
        var info = new SaveLocator(root).Scan().Single(c => c.Folder == "character_2").Slots.Single(s => s.SlotFolder == slot);
        var backupRoot = Path.Combine(Path.GetDirectoryName(root)!, "backups");
        return (info, new BackupService(backupRoot), backupRoot);
    }

    private static string DataSav(SlotInfo slot) => Path.Combine(slot.FullPath, "data.sav");

    private static GmObject Character(GmObject root) => (GmObject)root["characterDataMap"]!;

    private static string StrRawOnDisk(SlotInfo slot)
    {
        var decoded = SaveCodec.Decode(File.ReadAllBytes(DataSav(slot)));
        return ((GmNumber)Character((GmObject)GmJson.Parse(decoded.Json))["STR"]!).Raw;
    }

    private static void SetStr(SaveSession session, double value)
    {
        Character(session.Root).Set("STR", GmNumber.From(value));
        session.MarkChanged();
    }

    [Fact]
    public void Open_reads_root_and_starts_clean()
    {
        var (slot, _, _) = Setup();
        var session = SaveSession.Open(slot);
        Assert.Equal(0, session.ChangeCount);
        Assert.False(session.ReadOnly);
        Assert.NotNull(session.Root["characterDataMap"]);
    }

    [Fact]
    public void Save_writes_valid_file_and_backup()
    {
        var (slot, backups, backupRoot) = Setup();
        var session = SaveSession.Open(slot);
        SetStr(session, 14);

        Assert.Equal(SaveResult.Saved, session.Save(backups, NotRunning));

        var decoded = SaveCodec.Decode(File.ReadAllBytes(DataSav(slot)));
        Assert.True(SaveCodec.HashMatches(decoded, "stOne!characters_v1!character_2!exitsave_1!shArd"));
        Assert.Equal("14.0", StrRawOnDisk(slot));
        Assert.Equal(0, session.ChangeCount);

        var zip = Assert.Single(Directory.GetFiles(Path.Combine(backupRoot, "character_2", "exitsave_1"), "*.zip"));
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(new[] { "data.sav", "preview.png", "save.map" }, archive.Entries.Select(e => e.FullName).Order());
    }

    [Fact]
    public void Save_refused_while_game_running()
    {
        var (slot, backups, backupRoot) = Setup();
        var before = File.ReadAllBytes(DataSav(slot));
        var session = SaveSession.Open(slot);
        SetStr(session, 14);

        Assert.Equal(SaveResult.GameRunning, session.Save(backups, new FakeProbe(true)));

        Assert.Equal(before, File.ReadAllBytes(DataSav(slot)));
        Assert.False(Directory.Exists(backupRoot));
        Assert.Equal(1, session.ChangeCount);
    }

    [Fact]
    public void Save_refused_when_file_changed_on_disk()
    {
        var (slot, backups, _) = Setup();
        var session = SaveSession.Open(slot);
        SetStr(session, 14);

        // The game writes the slot while the editor has it open.
        var external = (GmObject)GmJson.Parse(SaveCodec.Decode(File.ReadAllBytes(DataSav(slot))).Json);
        Character(external).Set("STR", GmNumber.From(99));
        File.WriteAllBytes(DataSav(slot), SaveCodec.Encode(GmJson.Write(external), Salt.For(SaveFileKind.SlotFile, "character_2", "exitsave_1")));

        Assert.Equal(SaveResult.ChangedOnDisk, session.Save(backups, NotRunning));
        Assert.Equal("99.0", StrRawOnDisk(slot));
    }

    [Fact]
    public void ReadOnly_session_never_writes()
    {
        var (slot, backups, _) = Setup();
        var readOnlySlot = slot with { Status = SlotStatus.UnsupportedVersion, StatusMessage = "Save version 0.10 is not supported" };
        var before = File.ReadAllBytes(DataSav(slot));
        var session = SaveSession.Open(readOnlySlot);
        SetStr(session, 14);

        Assert.True(session.ReadOnly);
        Assert.Equal("Save version 0.10 is not supported", session.ReadOnlyReason);
        Assert.Equal(SaveResult.ReadOnly, session.Save(backups, NotRunning));
        Assert.Equal(before, File.ReadAllBytes(DataSav(slot)));
    }

    [Fact]
    public void Undecodable_slot_cannot_be_opened()
    {
        var (slot, _, _) = Setup();
        File.WriteAllText(DataSav(slot), "junk");
        Assert.Throws<SaveFormatException>(() => SaveSession.Open(slot with { Status = SlotStatus.Damaged }));
    }

    [Fact]
    public void Unchanged_save_rewrites_identical_payload()
    {
        var (slot, backups, _) = Setup();
        var original = SaveCodec.Decode(File.ReadAllBytes(DataSav(slot)));
        var session = SaveSession.Open(slot);
        session.MarkChanged();

        Assert.Equal(SaveResult.Saved, session.Save(backups, NotRunning));

        var after = SaveCodec.Decode(File.ReadAllBytes(DataSav(slot)));
        Assert.Equal(original.Json, after.Json);
        Assert.Equal(original.StoredHash, after.StoredHash);
    }

    [Fact]
    public void Second_save_after_first_succeeds()
    {
        var (slot, backups, _) = Setup();
        var session = SaveSession.Open(slot);
        SetStr(session, 14);
        Assert.Equal(SaveResult.Saved, session.Save(backups, NotRunning));
        SetStr(session, 15);
        Assert.Equal(SaveResult.Saved, session.Save(backups, NotRunning));
        Assert.Equal("15.0", StrRawOnDisk(slot));
    }

    [Fact]
    public void Restore_brings_back_backup_and_backs_up_current_first()
    {
        var (slot, backups, _) = Setup();
        var session = SaveSession.Open(slot);
        SetStr(session, 14);
        session.Save(backups, NotRunning);   // backup holds STR 13
        SetStr(session, 15);
        session.Save(backups, NotRunning);   // backup holds STR 14

        var list = backups.List("character_2", "exitsave_1");
        Assert.Equal(2, list.Count);
        Assert.True(list[0].CreatedUtc >= list[1].CreatedUtc);

        backups.Restore(list[1], slot);

        Assert.Equal("13.0", StrRawOnDisk(slot));
        Assert.Equal(3, backups.List("character_2", "exitsave_1").Count);
    }
}

using System.Security.Cryptography;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Core.Files;

public enum SaveResult { Saved, GameRunning, ChangedOnDisk, ReadOnly }

/// <summary>One opened save slot: its parsed <c>data.sav</c>, the edit counter, and guarded writing.</summary>
public sealed class SaveSession
{
    private byte[] _baselineHash;

    private SaveSession(SlotInfo slot, GmObject root, byte[] baselineHash)
    {
        Slot = slot;
        Root = root;
        _baselineHash = baselineHash;
    }

    public SlotInfo Slot { get; }
    public GmObject Root { get; }
    public bool ReadOnly => Slot.Status != SlotStatus.Ok;
    public string? ReadOnlyReason => ReadOnly ? Slot.StatusMessage ?? "This save can't be written safely" : null;
    public int ChangeCount { get; private set; }

    private string DataSavPath => Path.Combine(Slot.FullPath, "data.sav");

    public static SaveSession Open(SlotInfo slot)
    {
        var bytes = File.ReadAllBytes(Path.Combine(slot.FullPath, "data.sav"));
        var decoded = SaveCodec.Decode(bytes);
        var root = GmJson.Parse(decoded.Json) as GmObject ?? throw new SaveFormatException("data.sav is not a JSON object");
        return new SaveSession(slot, root, SHA256.HashData(bytes));
    }

    public void MarkChanged() => ChangeCount++;

    public SaveResult Save(BackupService backups, IGameProcessProbe probe)
    {
        if (ReadOnly)
            return SaveResult.ReadOnly;
        if (probe.IsGameRunning())
            return SaveResult.GameRunning;
        if (!SHA256.HashData(File.ReadAllBytes(DataSavPath)).AsSpan().SequenceEqual(_baselineHash))
            return SaveResult.ChangedOnDisk;

        backups.Backup(Slot);

        var encoded = SaveCodec.Encode(GmJson.Write(Root), Salt.For(SaveFileKind.SlotFile, Slot.CharacterFolder, Slot.SlotFolder));
        var tmp = DataSavPath + ".tmp";
        File.WriteAllBytes(tmp, encoded);
        File.Replace(tmp, DataSavPath, destinationBackupFileName: null);

        _baselineHash = SHA256.HashData(encoded);
        ChangeCount = 0;
        return SaveResult.Saved;
    }
}

using System.Globalization;
using System.IO.Compression;

namespace StoneshardSaveEditor.Core.Files;

public sealed record BackupInfo(string Path, DateTime CreatedUtc);

/// <summary>Zips a slot folder before every write, outside the game's folder so neither the game nor Steam Cloud sees it.</summary>
public sealed class BackupService(string backupRoot)
{
    private const string StampFormat = "yyyyMMdd-HHmmss-fff";

    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StoneShardSaveEditor", "backups");

    public string Backup(SlotInfo slot)
    {
        var dir = Path.Combine(backupRoot, slot.CharacterFolder, slot.SlotFolder);
        Directory.CreateDirectory(dir);
        var stamp = DateTime.UtcNow;
        string zip;
        // Two backups in the same millisecond would collide; nudge the stamp forward.
        while (File.Exists(zip = Path.Combine(dir, stamp.ToString(StampFormat, CultureInfo.InvariantCulture) + ".zip")))
            stamp = stamp.AddMilliseconds(1);
        ZipFile.CreateFromDirectory(slot.FullPath, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
        return zip;
    }

    public IReadOnlyList<BackupInfo> List(string characterFolder, string slotFolder)
    {
        var dir = Path.Combine(backupRoot, characterFolder, slotFolder);
        if (!Directory.Exists(dir))
            return [];
        return Directory.EnumerateFiles(dir, "*.zip")
            .Select(path => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), StampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var created)
                ? new BackupInfo(path, created)
                : null)
            .OfType<BackupInfo>()
            .OrderByDescending(b => b.CreatedUtc)
            .ToList();
    }

    public void Restore(BackupInfo backup, SlotInfo slot)
    {
        Backup(slot);
        using var archive = ZipFile.OpenRead(backup.Path);
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(slot.FullPath, entry.FullName));
            if (!target.StartsWith(Path.GetFullPath(slot.FullPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Backup entry {entry.FullName} points outside the slot folder");
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}

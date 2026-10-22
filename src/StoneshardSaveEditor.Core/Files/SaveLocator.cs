using System.Text.RegularExpressions;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Core.Files;

public enum SlotStatus { Ok, Damaged, UnsupportedVersion }

public sealed record SlotInfo(
    string CharacterFolder,
    string SlotFolder,
    string FullPath,
    DateTime SavedAtUtc,
    string LocationKey,
    string AvatarSprite,
    string NameKey,
    SlotStatus Status,
    string? StatusMessage);

public sealed record CharacterInfo(
    string Folder,
    string NameKey,
    string? ClassKey,
    int? Level,
    string AvatarSprite,
    IReadOnlyList<SlotInfo> Slots);

/// <summary>Finds characters and their save slots under <c>characters_v1</c>, checking every slot's checksum.</summary>
public sealed partial class SaveLocator(string root)
{
    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StoneShard", "characters_v1");

    public IReadOnlyList<CharacterInfo> Scan()
    {
        if (!Directory.Exists(root))
            return [];

        return Directory.EnumerateDirectories(root)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => CharacterFolderPattern().IsMatch(name))
            .OrderBy(name => int.Parse(name["character_".Length..]))
            .Select(ScanCharacter)
            .ToList();
    }

    private CharacterInfo ScanCharacter(string folder)
    {
        var path = Path.Combine(root, folder);
        var slots = Directory.EnumerateDirectories(path)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => SlotFolderPattern().IsMatch(name))
            .Select(slot => ScanSlot(folder, slot))
            .OrderByDescending(s => s.SavedAtUtc)
            .ToList();

        var nameKey = ReadMap(Path.Combine(path, "character.map"), Salt.For(SaveFileKind.CharacterMap, folder))?["nameKey"] is GmString n
            ? n.Value
            : slots.FirstOrDefault(s => s.NameKey != "")?.NameKey ?? folder;

        string? classKey = null;
        int? level = null;
        var newestOk = slots.FirstOrDefault(s => s.Status == SlotStatus.Ok);
        if (newestOk is not null)
        {
            var data = ReadMap(Path.Combine(newestOk.FullPath, "data.sav"), Salt.For(SaveFileKind.SlotFile, folder, newestOk.SlotFolder));
            if (data?["characterDataMap"] is GmObject character)
            {
                classKey = (character["classKey"] as GmString)?.Value;
                level = character["LVL"] is GmNumber lvl ? (int)lvl.Value : null;
            }
        }

        var avatar = slots.FirstOrDefault(s => s.AvatarSprite != "")?.AvatarSprite ?? "";
        return new CharacterInfo(folder, nameKey, classKey, level, avatar, slots);
    }

    private SlotInfo ScanSlot(string character, string slot)
    {
        var path = Path.Combine(root, character, slot);
        var salt = Salt.For(SaveFileKind.SlotFile, character, slot);
        var fallbackTime = Directory.GetLastWriteTimeUtc(path);

        GmObject map;
        try
        {
            map = DecodeVerified(Path.Combine(path, "save.map"), salt);
        }
        catch (Exception e) when (e is SaveFormatException or IOException or FormatException)
        {
            return new SlotInfo(character, slot, path, fallbackTime, "", "", "", SlotStatus.Damaged, $"save.map: {e.Message}");
        }

        var savedAt = map["dateTime"] is GmNumber dt ? GameDate.ToUtc(dt.Value) : fallbackTime;
        var location = (map["locationTitleKey"] as GmString)?.Value ?? "";
        var avatar = (map["avatar"] as GmString)?.Value ?? "";
        var name = (map["nameKey"] as GmString)?.Value ?? "";
        var info = new SlotInfo(character, slot, path, savedAt, location, avatar, name, SlotStatus.Ok, null);

        try
        {
            var data = SaveCodec.Decode(File.ReadAllBytes(Path.Combine(path, "data.sav")));
            if (!SaveCodec.HashMatches(data, salt))
                return info with { Status = SlotStatus.Damaged, StatusMessage = "data.sav: the checksum doesn't match, the game would reject this save" };
        }
        catch (Exception e) when (e is SaveFormatException or IOException)
        {
            return info with { Status = SlotStatus.Damaged, StatusMessage = $"data.sav: {e.Message}" };
        }

        var version = (map["wipeVersion"] as GmString)?.Value;
        if (version != SupportedVersions.WipeVersion)
            return info with
            {
                Status = SlotStatus.UnsupportedVersion,
                StatusMessage = $"Save version {version ?? "unknown"} is not supported (expected {SupportedVersions.WipeVersion})",
            };

        return info;
    }

    private static GmObject? ReadMap(string file, string salt)
    {
        try
        {
            return DecodeVerified(file, salt);
        }
        catch (Exception e) when (e is SaveFormatException or IOException or FormatException)
        {
            return null;
        }
    }

    private static GmObject DecodeVerified(string file, string salt)
    {
        var decoded = SaveCodec.Decode(File.ReadAllBytes(file));
        if (!SaveCodec.HashMatches(decoded, salt))
            throw new SaveFormatException("the checksum doesn't match, the game would reject this save");
        return GmJson.Parse(decoded.Json) as GmObject ?? throw new SaveFormatException("expected a JSON object");
    }

    [GeneratedRegex(@"^character_\d+$")]
    private static partial Regex CharacterFolderPattern();

    [GeneratedRegex(@"^(exitsave|save|autosave)_\d+$")]
    private static partial Regex SlotFolderPattern();
}

using System.Globalization;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Core.Files;

/// <summary>
/// Makes throwaway copies of a character for in-game experiments. Because the checksum salt contains the folder names,
/// every copied file is re-signed for its new location.
/// </summary>
public static class TestCharacterCloner
{
    public const string TargetSlot = "exitsave_1";

    /// <summary>character_1 and character_2 are the owner's real characters and are never written.</summary>
    private const int FirstTestCharacter = 3;

    /// <param name="minutesLater">Shifts the save time shown in the game's menus, so several test characters sort in a known order.</param>
    public static string Clone(string root, string fromCharacter, string fromSlot, string? locationLabel = null, int minutesLater = 0)
    {
        // The game lists character_1..character_10 only (session C), so take the lowest free number; 1 and 2 stay the owner's.
        var used = Directory.EnumerateDirectories(root, "character_*")
            .Select(d => int.TryParse(Path.GetFileName(d)["character_".Length..], out var n) ? n : 0)
            .ToHashSet();
        var next = Enumerable.Range(FirstTestCharacter, int.MaxValue - FirstTestCharacter).First(n => !used.Contains(n));
        var character = $"character_{next}";
        var sourceSlot = Path.Combine(root, fromCharacter, fromSlot);
        var targetSlot = Path.Combine(root, character, TargetSlot);
        Directory.CreateDirectory(targetSlot);

        Action<GmObject> shiftTime = map =>
        {
            if (minutesLater != 0 && map["dateTime"] is GmNumber t)
                map.Set("dateTime", GmNumber.From(t.Value + minutesLater / (24.0 * 60)));
        };
        Resign(Path.Combine(root, fromCharacter, "character.map"), Salt.For(SaveFileKind.CharacterMap, fromCharacter),
            Path.Combine(root, character, "character.map"), Salt.For(SaveFileKind.CharacterMap, character),
            minutesLater != 0 ? shiftTime : null);

        var slotSalt = Salt.For(SaveFileKind.SlotFile, character, TargetSlot);
        var sourceSalt = Salt.For(SaveFileKind.SlotFile, fromCharacter, fromSlot);
        Resign(Path.Combine(sourceSlot, "save.map"), sourceSalt, Path.Combine(targetSlot, "save.map"), slotSalt, map =>
        {
            if (locationLabel is not null)
                map.Set("locationTitleKey", GmString.From(locationLabel));
            shiftTime(map);
        });
        Resign(Path.Combine(sourceSlot, "data.sav"), sourceSalt, Path.Combine(targetSlot, "data.sav"), slotSalt);
        var preview = Path.Combine(sourceSlot, "preview.png");
        if (File.Exists(preview))
            File.Copy(preview, Path.Combine(targetSlot, "preview.png"));

        return character;
    }

    /// <summary>Points the game's "Continue" button at a character and slot, keeping a backup of <c>characters.map</c>.</summary>
    public static void PointContinue(string root, string character, string slot, string backupDir)
    {
        var file = Path.Combine(root, "characters.map");
        Directory.CreateDirectory(backupDir);
        File.Copy(file, Path.Combine(backupDir, "characters.map." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)));

        var salt = Salt.For(SaveFileKind.CharactersMap);
        Resign(file, salt, file, salt, map =>
        {
            map.Set("lastCharacter", GmString.From(character));
            map.Set("lastSave", GmString.From(slot));
        });
    }

    private static void Resign(string source, string sourceSalt, string target, string targetSalt, Action<GmObject>? edit = null)
    {
        var decoded = SaveCodec.Decode(File.ReadAllBytes(source));
        if (!SaveCodec.HashMatches(decoded, sourceSalt))
            throw new SaveFormatException($"{source}: the checksum doesn't match");
        var json = decoded.Json;
        if (edit is not null)
        {
            var root = GmJson.Parse(json) as GmObject ?? throw new SaveFormatException($"{source} is not a JSON object");
            edit(root);
            json = GmJson.Write(root);
        }
        File.WriteAllBytes(target, SaveCodec.Encode(json, targetSalt));
    }
}

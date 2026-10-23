using System.Globalization;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Core.Files;

/// <summary>
/// Makes throwaway copies of a character for in-game experiments. Because the checksum salt contains the folder names,
/// every copied file is re-signed for its new location.
/// </summary>
public static class TestCharacterCloner
{
    /// <summary>A regular save: the game keeps it when it loads it, while loading an exit save consumes it (sessions A–C).</summary>
    public const string TargetSlot = "save_1";

    /// <summary>character_1 and character_2 are the owner's real characters and are never written.</summary>
    private const int FirstTestCharacter = 3;

    /// <summary>The game lists character_1 to character_10 only (session C).</summary>
    private const int LastListedCharacter = 10;

    /// <param name="minutesLater">Shifts the save time shown in the game's menus, so several test characters sort in a known order.</param>
    public static string Clone(string root, string fromCharacter, string fromSlot, string? locationLabel = null, int minutesLater = 0)
    {
        // The game lists character_1..character_10 only (session C), so take the lowest free number; 1 and 2 stay the owner's.
        var used = Directory.EnumerateDirectories(root, "character_*")
            .Select(d => int.TryParse(Path.GetFileName(d)["character_".Length..], out var n) ? n : 0)
            .ToHashSet();
        var next = Enumerable.Range(FirstTestCharacter, LastListedCharacter - FirstTestCharacter + 1).FirstOrDefault(n => !used.Contains(n));
        if (next == 0)
            throw new InvalidOperationException(
                $"Every character slot the game lists (character_{FirstTestCharacter} to character_{LastListedCharacter}) is in use; delete a test character first");
        var character = $"character_{next}";
        RequireSlot(root, fromCharacter, fromSlot, Path.Combine(root, fromCharacter, "character.map"));
        Directory.CreateDirectory(Path.Combine(root, character));

        Action<GmObject> shiftTime = map =>
        {
            if (minutesLater != 0 && map["dateTime"] is GmNumber t)
                map.Set("dateTime", GmNumber.From(t.Value + minutesLater / (24.0 * 60)));
        };
        Resign(Path.Combine(root, fromCharacter, "character.map"), Salt.For(SaveFileKind.CharacterMap, fromCharacter),
            Path.Combine(root, character, "character.map"), Salt.For(SaveFileKind.CharacterMap, character),
            minutesLater != 0 ? shiftTime : null);

        CopySlot(root, fromCharacter, fromSlot, character, TargetSlot, map =>
        {
            if (locationLabel is not null)
                map.Set("locationTitleKey", GmString.From(locationLabel));
            shiftTime(map);
        });
        return character;
    }

    /// <summary>
    /// Copies a test character's slot to its next free regular save (<c>save_N</c>), so a state the game left in an exit
    /// save can be loaded again and again. The owner's characters are refused.
    /// </summary>
    /// <returns>The new slot's folder name.</returns>
    public static string KeepAsSave(string root, string character, string slot)
    {
        if (!character.StartsWith("character_", StringComparison.Ordinal)
            || !int.TryParse(character["character_".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < FirstTestCharacter)
            throw new InvalidOperationException($"{character} isn't a test character; only character_{FirstTestCharacter} and up are written");
        RequireSlot(root, character, slot);
        var target = $"save_{Enumerable.Range(1, int.MaxValue - 1).First(i => !Directory.Exists(Path.Combine(root, character, $"save_{i}")))}";
        CopySlot(root, character, slot, character, target, null);
        return target;
    }

    private static void RequireSlot(string root, string character, string slot, params string[] more)
    {
        var folder = Path.Combine(root, character, slot);
        foreach (var required in more.Append(Path.Combine(folder, "save.map")).Append(Path.Combine(folder, "data.sav")))
            if (!File.Exists(required))
                throw new FileNotFoundException($"Can't copy {character}/{slot}: {Path.GetFileName(required)} is missing", required);
    }

    private static void CopySlot(string root, string fromCharacter, string fromSlot, string toCharacter, string toSlot, Action<GmObject>? editMap)
    {
        var source = Path.Combine(root, fromCharacter, fromSlot);
        var target = Path.Combine(root, toCharacter, toSlot);
        Directory.CreateDirectory(target);
        var sourceSalt = Salt.For(SaveFileKind.SlotFile, fromCharacter, fromSlot);
        var targetSalt = Salt.For(SaveFileKind.SlotFile, toCharacter, toSlot);
        Resign(Path.Combine(source, "save.map"), sourceSalt, Path.Combine(target, "save.map"), targetSalt, editMap);
        Resign(Path.Combine(source, "data.sav"), sourceSalt, Path.Combine(target, "data.sav"), targetSalt);
        var preview = Path.Combine(source, "preview.png");
        if (File.Exists(preview))
            File.Copy(preview, Path.Combine(target, "preview.png"));
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

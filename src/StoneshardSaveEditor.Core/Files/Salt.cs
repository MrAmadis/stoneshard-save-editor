namespace StoneshardSaveEditor.Core.Files;

public enum SaveFileKind
{
    /// <summary><c>characters_v1/characters.map</c></summary>
    CharactersMap,
    /// <summary><c>characters_v1/character_N/character.map</c></summary>
    CharacterMap,
    /// <summary><c>save.map</c> or <c>data.sav</c> inside a slot folder.</summary>
    SlotFile,
}

/// <summary>The checksum salts the game appends before hashing (verified against real saves).</summary>
public static class Salt
{
    public static string For(SaveFileKind kind, string? characterFolder = null, string? slotFolder = null) => kind switch
    {
        SaveFileKind.CharactersMap => "stOne!characters_v1!shArd",
        SaveFileKind.CharacterMap => $"stOne!characters_v1!{Require(characterFolder)}!shArd",
        SaveFileKind.SlotFile => $"stOne!characters_v1!{Require(characterFolder)}!{Require(slotFolder)}!shArd",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string Require(string? folder) =>
        string.IsNullOrEmpty(folder) ? throw new ArgumentException("Folder name is required for this file kind") : folder;
}

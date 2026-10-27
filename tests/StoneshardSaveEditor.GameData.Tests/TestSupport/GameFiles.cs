using StoneshardSaveEditor.GameData;

namespace StoneshardSaveEditor.Tests;

/// <summary>Loads the real game files once per test run; every accessor skips the test when the game isn't installed.</summary>
public static class GameFiles
{
    private static readonly Lazy<DataWinFile> LazyDataWin = new(() => DataWinFile.Load(Path.Combine(Fixtures.GameDir, "data.win")));
    private static readonly Lazy<SpriteTable> LazySprites = new(() => new SpriteTable(LazyDataWin.Value));
    private static readonly Lazy<ObjectTable> LazyObjects = new(() => new ObjectTable(LazyDataWin.Value, LazySprites.Value));
    private static readonly Lazy<byte[]> LazyStrings = new(() => GameStrings.Load(Fixtures.GameDir, LazyDataWin.Value));
    private static readonly Lazy<ExeTables> LazyTables = new(() => ExeTables.Parse(LazyStrings.Value));
    private static readonly Lazy<Localization> LazyLocalization = new(() => Localization.Parse(LazyStrings.Value));

    public static DataWinFile DataWin => Ensure(LazyDataWin);
    public static SpriteTable Sprites => Ensure(LazySprites);
    public static ObjectTable Objects => Ensure(LazyObjects);
    public static ExeTables Tables => Ensure(LazyTables);
    public static Localization Localization => Ensure(LazyLocalization);

    private static T Ensure<T>(Lazy<T> lazy)
    {
        _ = Fixtures.GameDir; // skips when the install is missing
        return lazy.Value;
    }
}

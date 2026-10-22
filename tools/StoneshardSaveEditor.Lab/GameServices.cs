using StoneshardSaveEditor.Core.Json;
using StoneshardSaveEditor.GameData;

namespace StoneshardSaveEditor.Lab;

/// <summary>Everything read from the game install that the experiments need, loaded once.</summary>
public sealed class GameServices
{
    private GameServices(SpriteTable sprites, ObjectTable objects, ExeTables tables, Localization localization)
    {
        Objects = objects;
        Tables = tables;
        Localization = localization;
        Shapes = new GameItemShapes(sprites, objects, tables.ItemsStats);
        Catalog = new ItemCatalog(tables, localization, sprites, objects);
    }

    public ObjectTable Objects { get; }
    public ExeTables Tables { get; }
    public Localization Localization { get; }
    public GameItemShapes Shapes { get; }
    public ItemCatalog Catalog { get; }

    public static GameServices Load(string gameDir)
    {
        var dataWin = DataWinFile.Load(Path.Combine(gameDir, "data.win"));
        var sprites = new SpriteTable(dataWin);
        var exe = File.ReadAllBytes(Path.Combine(gameDir, "StoneShard.exe"));
        return new GameServices(sprites, new ObjectTable(dataWin, sprites), ExeTables.Parse(exe), Localization.Parse(exe));
    }

    public SkillCatalog SkillsFor(GmObject saveRoot) => SkillCatalog.Build(saveRoot, Tables, Localization, Objects);
}

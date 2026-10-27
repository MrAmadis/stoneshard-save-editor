using System.Collections.Concurrent;
using StoneshardSaveEditor.GameData;

namespace StoneshardSaveEditor.UI.Services;

/// <summary>Everything read from the Stoneshard install.</summary>
public sealed record GameFiles(
    string GameDir,
    ObjectTable Objects,
    ExeTables Tables,
    Localization Localization,
    IconService Icons,
    GameItemShapes Shapes,
    ItemCatalog Catalog);

/// <summary>
/// Finds and loads the game files once. Any failure (install missing, or a game update the readers don't understand)
/// is reported as <see cref="Error"/> instead of thrown, so the rest of the editor keeps working.
/// </summary>
public sealed class GameDataProvider(Func<string?> configuredDir, string cacheRoot)
{
    // Loading reads ~280 MB; share it within the process (tests create many providers).
    private static readonly ConcurrentDictionary<string, Lazy<GameFiles>> Loaded = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _lock = new();
    private bool _resolved;
    private GameFiles? _files;
    private string? _error;
    private string? _gameDir;

    public string CacheRoot => cacheRoot;

    public GameFiles? Files
    {
        get
        {
            Resolve();
            return _files;
        }
    }

    public string? Error
    {
        get
        {
            Resolve();
            return _error;
        }
    }

    public string? GameDir
    {
        get
        {
            Resolve();
            return _gameDir;
        }
    }

    /// <summary>Forget the current result, e.g. after the user picked another game folder.</summary>
    public void Reset()
    {
        lock (_lock)
            _resolved = false;
    }

    private void Resolve()
    {
        lock (_lock)
        {
            if (_resolved)
                return;
            _resolved = true;
            _files = null;
            _error = null;
            _gameDir = GameInstall.Find(configuredDir());
            if (_gameDir is null)
            {
                _error = "The Stoneshard install was not found";
                return;
            }

            var key = _gameDir + "|" + cacheRoot;
            try
            {
                var dir = _gameDir;
                _files = Loaded.GetOrAdd(key, _ => new Lazy<GameFiles>(() => Load(dir, cacheRoot))).Value;
            }
            catch (Exception e)
            {
                Loaded.TryRemove(key, out _);
                _error = $"Couldn't read the game files: {e.Message}";
            }
        }
    }

    private static GameFiles Load(string gameDir, string cacheRoot)
    {
        var dataWinPath = Path.Combine(gameDir, "data.win");
        var dataWin = DataWinFile.Load(dataWinPath);
        var sprites = new SpriteTable(dataWin);
        var objects = new ObjectTable(dataWin, sprites);
        var strings = GameStrings.Load(gameDir, dataWin);
        var tables = ExeTables.Parse(strings);
        var localization = Localization.Parse(strings);
        var icons = new IconService(sprites, new TexturePages(dataWin), IconService.CacheDirFor(cacheRoot, dataWinPath));
        return new GameFiles(gameDir, objects, tables, localization, icons,
            new GameItemShapes(sprites, objects, tables), new ItemCatalog(tables, localization, sprites, objects));
    }
}

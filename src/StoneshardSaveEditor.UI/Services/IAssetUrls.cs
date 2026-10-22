namespace StoneshardSaveEditor.UI.Services;

/// <summary>URLs for game icons and save screenshots, which the window serves as plain files.</summary>
public interface IAssetUrls
{
    string Icon(string sprite);
    string Preview(string character, string slot);
}

/// <summary>
/// Serves files through WebView2 virtual host names: <see cref="IconsHost"/> maps to the icon cache root and
/// <see cref="SavesHost"/> to the saves folder. Icons are extracted from the game files on first use.
/// </summary>
public sealed class VirtualHostAssets(GameDataProvider game) : IAssetUrls
{
    public const string IconsHost = "icons.sse";
    public const string SavesHost = "saves.sse";

    /// <summary>A transparent pixel for sprites the game files don't have.</summary>
    public const string Blank = "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

    public string Icon(string sprite)
    {
        var icons = game.Files?.Icons;
        if (icons is null || icons.GetPng(sprite) is null)
            return Blank;
        var relative = Path.GetRelativePath(game.CacheRoot, Path.Combine(icons.CacheDir, sprite + ".png")).Replace('\\', '/');
        return $"https://{IconsHost}/{relative}";
    }

    public string Preview(string character, string slot) => $"https://{SavesHost}/{character}/{slot}/preview.png";
}

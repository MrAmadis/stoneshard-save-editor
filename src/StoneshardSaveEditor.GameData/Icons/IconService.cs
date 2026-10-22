using System.Text.RegularExpressions;

namespace StoneshardSaveEditor.GameData;

/// <summary>
/// Serves a sprite's first frame as a PNG, cut from its texture page and cached on disk so later launches are instant.
/// </summary>
public sealed partial class IconService(SpriteTable sprites, TexturePages pages, string cacheDir)
{
    private readonly Lock _lock = new();

    /// <summary>A cache folder tied to one exact <c>data.win</c>, so a game update starts a fresh cache.</summary>
    public static string CacheDirFor(string cacheRoot, string dataWinPath)
    {
        var info = new FileInfo(dataWinPath);
        return Path.Combine(cacheRoot, $"{info.Length}-{info.LastWriteTimeUtc.Ticks}", "icons");
    }

    /// <summary>The sprite's canvas size in pixels (27 px per inventory cell).</summary>
    public (int W, int H)? Size(string sprite) => sprites.Get(sprite) switch
    {
        null => null,
        { FirstFrame: { } frame } => (frame.BoundW, frame.BoundH),
        var s => (s.Width, s.Height),
    };

    public byte[]? GetPng(string sprite)
    {
        if (!SpriteName().IsMatch(sprite))
            return null;
        var cached = Path.Combine(cacheDir, sprite + ".png");
        if (File.Exists(cached))
            return File.ReadAllBytes(cached);

        var image = GetRgba(sprite);
        if (image is null)
            return null;
        var png = Png.Encode(image);

        Directory.CreateDirectory(cacheDir);
        var tmp = cached + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(tmp, png);
        File.Move(tmp, cached, overwrite: true);
        return png;
    }

    internal Rgba32Image? GetRgba(string sprite)
    {
        var frame = sprites.Get(sprite)?.FirstFrame;
        if (frame is null)
            return null;

        Rgba32Image page;
        lock (_lock)
            page = pages.Page(frame.Texture);

        var canvas = new byte[frame.BoundW * frame.BoundH * 4];
        for (var ty = 0; ty < frame.TargetH; ty++)
        {
            var sy = frame.SourceY + ty * frame.SourceH / frame.TargetH;
            var cy = frame.TargetY + ty;
            if (cy < 0 || cy >= frame.BoundH) continue;
            for (var tx = 0; tx < frame.TargetW; tx++)
            {
                var sx = frame.SourceX + tx * frame.SourceW / frame.TargetW;
                var cx = frame.TargetX + tx;
                if (cx < 0 || cx >= frame.BoundW) continue;
                Array.Copy(page.Pixels, (sy * page.Width + sx) * 4, canvas, (cy * frame.BoundW + cx) * 4, 4);
            }
        }
        return new Rgba32Image(frame.BoundW, frame.BoundH, canvas);
    }

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex SpriteName();
}

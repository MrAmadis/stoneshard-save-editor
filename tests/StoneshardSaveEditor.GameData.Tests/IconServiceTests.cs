using System.Buffers.Binary;
using StoneshardSaveEditor.GameData;

namespace StoneshardSaveEditor.Tests;

public class IconServiceTests
{
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private static string NewCacheDir() => Path.Combine(Directory.CreateTempSubdirectory().FullName, "icons");

    [Fact]
    public void Sprite_sizes_match_footprints()
    {
        var icons = new IconService(GameFiles.Sprites, new TexturePages(GameFiles.DataWin), NewCacheDir());
        Assert.Equal((27, 81), icons.Size("s_inv_torch"));
        Assert.Equal((54, 81), icons.Size("s_inv_roadguardbrigandine"));
        Assert.Equal((37, 50), icons.Size("s_Velmir"));
        Assert.Equal((25, 24), icons.Size("s_skills_wide_cut"));
        Assert.Null(icons.Size("s_does_not_exist"));
    }

    [Fact]
    public void Icon_png_has_right_size_and_visible_pixels()
    {
        var icons = new IconService(GameFiles.Sprites, new TexturePages(GameFiles.DataWin), NewCacheDir());

        var png = icons.GetPng("s_inv_moneybag")!;
        Assert.Equal(PngSignature, png[..8]);
        Assert.Equal("IHDR"u8.ToArray(), png[12..16]);
        Assert.Equal(27, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(54, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));

        var rgba = icons.GetRgba("s_inv_moneybag")!;
        var visible = Enumerable.Range(0, rgba.Width * rgba.Height).Count(i => rgba.Pixels[i * 4 + 3] > 0);
        Assert.True(visible >= 100, $"only {visible} visible pixels");
        Assert.Null(icons.GetPng("s_does_not_exist"));
    }

    [Fact]
    public void Second_service_serves_from_disk_cache()
    {
        var cache = NewCacheDir();
        var first = new IconService(GameFiles.Sprites, new TexturePages(GameFiles.DataWin), cache).GetPng("s_inv_salve");
        var pages = new TexturePages(GameFiles.DataWin);

        var second = new IconService(GameFiles.Sprites, pages, cache).GetPng("s_inv_salve");

        Assert.Equal(first, second);
        Assert.Equal(0, pages.DecodeCount);
        Assert.True(File.Exists(Path.Combine(cache, "s_inv_salve.png")));
    }

    [Fact]
    public void Cache_dir_is_keyed_to_the_data_win_file()
    {
        var dataWin = Path.Combine(Fixtures.GameDir, "data.win");
        var info = new FileInfo(dataWin);
        Assert.Equal(
            Path.Combine(@"C:\cache", $"{info.Length}-{info.LastWriteTimeUtc.Ticks}", "icons"),
            IconService.CacheDirFor(@"C:\cache", dataWin));
    }

    [Fact]
    public void Png_encoder_writes_valid_ihdr_crc_and_pixels()
    {
        var png = Png.Encode(new Rgba32Image(1, 1, [255, 0, 0, 255]));

        // IHDR chunk: length(4) type(4) data(13) crc(4) starting at byte 8.
        // Every 1x1 RGBA8 PNG has the IHDR CRC 0x1F15C489.
        Assert.Equal(0x1F15C489u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(8 + 8 + 13)));

        // IDAT follows IHDR: length(4) "IDAT" zlib(filter byte 0 + RGBA)
        var idatLength = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33));
        Assert.Equal("IDAT"u8.ToArray(), png[37..41]);
        using var z = new System.IO.Compression.ZLibStream(new MemoryStream(png, 41, idatLength), System.IO.Compression.CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        Assert.Equal(new byte[] { 0, 255, 0, 0, 255 }, raw.ToArray());
    }
}

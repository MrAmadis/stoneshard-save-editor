using StoneshardSaveEditor.GameData;

namespace StoneshardSaveEditor.Tests;

public class GmQoiTests
{
    private static byte[] Qoi(int width, int height, params byte[] ops)
    {
        var header = new byte[12];
        "fioq"u8.CopyTo(header);
        BitConverter.TryWriteBytes(header.AsSpan(4), (ushort)width);
        BitConverter.TryWriteBytes(header.AsSpan(6), (ushort)height);
        BitConverter.TryWriteBytes(header.AsSpan(8), ops.Length);
        return [.. header, .. ops];
    }

    [Fact]
    public void GmQoi_decodes_color_then_run()
    {
        var img = GmQoi.Decode(Qoi(2, 1, 0xFF, 10, 20, 30, 255, 0x40));
        Assert.Equal(2, img.Width);
        Assert.Equal(1, img.Height);
        Assert.Equal(new byte[] { 10, 20, 30, 255, 10, 20, 30, 255 }, img.Pixels);
    }

    [Fact]
    public void GmQoi_applies_small_diff()
    {
        // COLOR (10,20,30,255), then DIFF_8 0b10_01_11_10 → dr=+1 (01), dg=-1 (11), db=-2 (10)
        var img = GmQoi.Decode(Qoi(2, 1, 0xFF, 10, 20, 30, 255, 0b1001_1110));
        Assert.Equal(new byte[] { 10, 20, 30, 255, 11, 19, 28, 255 }, img.Pixels);
    }

    [Fact]
    public void GmQoi_rejects_wrong_magic() =>
        Assert.Throws<InvalidDataException>(() => GmQoi.Decode("qoif\0\0\0\0\0\0\0\0"u8));
}

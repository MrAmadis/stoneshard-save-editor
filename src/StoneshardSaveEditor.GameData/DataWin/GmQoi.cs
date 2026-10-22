using System.Buffers.Binary;

namespace StoneshardSaveEditor.GameData;

/// <summary>
/// GameMaker's own QOI variant ("fioq"): a 12-byte header (magic, u16 width, u16 height, u32 data length)
/// followed by ops that differ from standard QOI. Bit layout taken from the verified spike decoder.
/// </summary>
public static class GmQoi
{
    public static Rgba32Image Decode(ReadOnlySpan<byte> qoi)
    {
        if (qoi.Length < 12 || !qoi[..4].SequenceEqual("fioq"u8))
            throw new InvalidDataException("Not a GameMaker QOI image");
        int width = BinaryPrimitives.ReadUInt16LittleEndian(qoi[4..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(qoi[6..]);
        var end = 12 + (int)BinaryPrimitives.ReadUInt32LittleEndian(qoi[8..]);
        if (end > qoi.Length)
            throw new InvalidDataException("QOI data is truncated");

        var pixels = new byte[width * height * 4];
        Span<byte> index = stackalloc byte[256];
        index.Clear();
        int r = 0, g = 0, b = 0, a = 255, run = 0, p = 12;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (run > 0)
            {
                run--;
            }
            else if (p < end)
            {
                int b1 = qoi[p++];
                if ((b1 & 0xC0) == 0x00)
                {
                    var ip = (b1 & 0x3F) << 2;
                    r = index[ip]; g = index[ip + 1]; b = index[ip + 2]; a = index[ip + 3];
                }
                else if ((b1 & 0xE0) == 0x40)
                {
                    run = b1 & 0x1F;
                }
                else if ((b1 & 0xE0) == 0x60)
                {
                    run = (((b1 & 0x1F) << 8) | qoi[p++]) + 32;
                }
                else if ((b1 & 0xC0) == 0x80)
                {
                    r += Signed(b1 >> 4, 2); g += Signed(b1 >> 2, 2); b += Signed(b1, 2);
                }
                else if ((b1 & 0xE0) == 0xC0)
                {
                    var m = (b1 << 8) | qoi[p++];
                    r += Signed(m >> 8, 5); g += Signed(m >> 4, 4); b += Signed(m, 4);
                }
                else if ((b1 & 0xF0) == 0xE0)
                {
                    var m = (b1 << 16) | (qoi[p] << 8) | qoi[p + 1];
                    p += 2;
                    r += Signed(m >> 15, 5); g += Signed(m >> 10, 5); b += Signed(m >> 5, 5); a += Signed(m, 5);
                }
                else
                {
                    if ((b1 & 8) != 0) r = qoi[p++];
                    if ((b1 & 4) != 0) g = qoi[p++];
                    if ((b1 & 2) != 0) b = qoi[p++];
                    if ((b1 & 1) != 0) a = qoi[p++];
                }
                r &= 255; g &= 255; b &= 255; a &= 255;
                var slot = ((r ^ g ^ b ^ a) & 63) << 2;
                index[slot] = (byte)r; index[slot + 1] = (byte)g; index[slot + 2] = (byte)b; index[slot + 3] = (byte)a;
            }
            pixels[i] = (byte)r; pixels[i + 1] = (byte)g; pixels[i + 2] = (byte)b; pixels[i + 3] = (byte)a;
        }
        return new Rgba32Image(width, height, pixels);
    }

    /// <summary>Sign-extends the low <paramref name="bits"/> bits of <paramref name="value"/>.</summary>
    private static int Signed(int value, int bits)
    {
        var v = value & ((1 << bits) - 1);
        return v >> (bits - 1) != 0 ? v - (1 << bits) : v;
    }
}

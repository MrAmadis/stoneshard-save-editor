using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace StoneshardSaveEditor.Core.Files;

public sealed record DecodedSave(byte[] Json, string StoredHash);

public sealed class SaveFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Stoneshard save files are a zlib stream of <c>JSON + 32 lowercase hex MD5 + NUL</c>,
/// where the MD5 covers the JSON followed by a folder-specific salt.
/// </summary>
public static class SaveCodec
{
    private const int HashLength = 32;

    public static DecodedSave Decode(byte[] file)
    {
        byte[] payload;
        try
        {
            using var z = new ZLibStream(new MemoryStream(file), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            z.CopyTo(ms);
            payload = ms.ToArray();
        }
        catch (InvalidDataException e)
        {
            throw new SaveFormatException("The file is not a valid compressed save", e);
        }

        if (payload.Length < HashLength + 1 + 2)
            throw new SaveFormatException("The save is too short to contain data and a checksum");
        if (payload[^1] != 0)
            throw new SaveFormatException("The save does not end with the expected terminator");

        var hash = Encoding.ASCII.GetString(payload, payload.Length - 1 - HashLength, HashLength);
        if (!hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new SaveFormatException("The save checksum is malformed");

        return new DecodedSave(payload[..^(HashLength + 1)], hash);
    }

    public static string ComputeHash(byte[] json, string salt)
    {
        var saltBytes = Encoding.UTF8.GetBytes(salt);
        var input = new byte[json.Length + saltBytes.Length];
        json.CopyTo(input, 0);
        saltBytes.CopyTo(input, json.Length);
        return Convert.ToHexStringLower(MD5.HashData(input));
    }

    public static bool HashMatches(DecodedSave decoded, string salt) =>
        string.Equals(decoded.StoredHash, ComputeHash(decoded.Json, salt), StringComparison.Ordinal);

    public static byte[] Encode(byte[] json, string salt)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal))
        {
            z.Write(json);
            z.Write(Encoding.ASCII.GetBytes(ComputeHash(json, salt)));
            z.WriteByte(0);
        }
        return ms.ToArray();
    }
}

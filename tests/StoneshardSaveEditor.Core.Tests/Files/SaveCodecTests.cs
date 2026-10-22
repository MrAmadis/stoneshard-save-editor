using System.IO.Compression;
using System.Text;
using StoneshardSaveEditor.Core.Files;

namespace StoneshardSaveEditor.Tests.Files;

public class SaveCodecTests
{
    private static string Slot(string name) => Path.Combine(Fixtures.RealSavesRoot, "character_2", "exitsave_1", name);

    [Fact]
    public void Salt_strings_match_game()
    {
        Assert.Equal("stOne!characters_v1!shArd", Salt.For(SaveFileKind.CharactersMap));
        Assert.Equal("stOne!characters_v1!character_2!shArd", Salt.For(SaveFileKind.CharacterMap, "character_2"));
        Assert.Equal("stOne!characters_v1!character_2!exitsave_1!shArd", Salt.For(SaveFileKind.SlotFile, "character_2", "exitsave_1"));
    }

    [Fact]
    public void Real_files_have_valid_hashes()
    {
        var root = Fixtures.RealSavesRoot;
        Assert.True(SaveCodec.HashMatches(SaveCodec.Decode(File.ReadAllBytes(Path.Combine(root, "characters.map"))),
            "stOne!characters_v1!shArd"));
        Assert.True(SaveCodec.HashMatches(SaveCodec.Decode(File.ReadAllBytes(Path.Combine(root, "character_2", "character.map"))),
            "stOne!characters_v1!character_2!shArd"));
        const string slotSalt = "stOne!characters_v1!character_2!exitsave_1!shArd";
        Assert.True(SaveCodec.HashMatches(SaveCodec.Decode(File.ReadAllBytes(Slot("save.map"))), slotSalt));
        var data = SaveCodec.Decode(File.ReadAllBytes(Slot("data.sav")));
        Assert.True(SaveCodec.HashMatches(data, slotSalt));
        Assert.Equal("1286653c2cd1cbdeee110fd96dff7970", data.StoredHash);
    }

    [Fact]
    public void Hash_does_not_match_another_slots_salt()
    {
        var data = SaveCodec.Decode(File.ReadAllBytes(Slot("data.sav")));
        Assert.False(SaveCodec.HashMatches(data, "stOne!characters_v1!character_2!save_1!shArd"));
    }

    [Fact]
    public void Reencoding_real_data_sav_yields_same_payload()
    {
        const string salt = "stOne!characters_v1!character_2!exitsave_1!shArd";
        var original = SaveCodec.Decode(File.ReadAllBytes(Slot("data.sav")));
        var again = SaveCodec.Decode(SaveCodec.Encode(original.Json, salt));
        Assert.Equal(original.Json, again.Json);
        Assert.Equal(original.StoredHash, again.StoredHash);
    }

    [Fact]
    public void Encode_writes_zlib_with_json_hash_and_nul()
    {
        var json = "{ \"a\": 1.0 }"u8.ToArray();
        var payload = Inflate(SaveCodec.Encode(json, "salt"));
        // md5("{ \"a\": 1.0 }salt") computed independently
        var expectedHash = Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData("{ \"a\": 1.0 }salt"u8));
        Assert.Equal("{ \"a\": 1.0 }" + expectedHash + "\0", Encoding.UTF8.GetString(payload));
    }

    [Fact]
    public void Decode_rejects_garbage()
    {
        Assert.Throws<SaveFormatException>(() => SaveCodec.Decode(Encoding.ASCII.GetBytes("definitely not a save file")));

        var real = File.ReadAllBytes(Slot("data.sav"));
        Assert.Throws<SaveFormatException>(() => SaveCodec.Decode(real[..(real.Length / 2)]));

        var hash = new string('a', 32);
        Assert.Throws<SaveFormatException>(() => SaveCodec.Decode(Deflate("{ }" + hash)));          // no trailing NUL
        Assert.Throws<SaveFormatException>(() => SaveCodec.Decode(Deflate("{ }" + new string('z', 32) + "\0"))); // non-hex hash
        Assert.Throws<SaveFormatException>(() => SaveCodec.Decode(Deflate("short\0")));             // too short
    }

    private static byte[] Deflate(string payload)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal))
            z.Write(Encoding.UTF8.GetBytes(payload));
        return ms.ToArray();
    }

    private static byte[] Inflate(byte[] data)
    {
        using var z = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        z.CopyTo(ms);
        return ms.ToArray();
    }
}

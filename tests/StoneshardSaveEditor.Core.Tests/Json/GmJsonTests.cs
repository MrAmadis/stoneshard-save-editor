using System.Text;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Tests.Json;

public class GmJsonTests
{
    [Fact]
    public void Parse_then_Write_reproduces_gamemaker_text_exactly()
    {
        const string text = "{ \"a\": 1.0, \"b\": [ ], \"c\": { }, \"d\": [ \"x\\/y\", -4.0, true, false, null ], \"e\": \"q\\\"\\\\\" }";
        Assert.Equal(text, Encoding.UTF8.GetString(GmJson.Write(GmJson.Parse(Encoding.UTF8.GetBytes(text)))));
    }

    [Fact]
    public void Every_real_save_file_round_trips_byte_for_byte()
    {
        var names = new[] { "data.sav", "save.map", "character.map", "characters.map" };
        var files = Directory.EnumerateFiles(Fixtures.RealSavesRoot, "*", SearchOption.AllDirectories)
            .Where(f => names.Contains(Path.GetFileName(f)))
            .ToList();
        // 9 slot folders x (data.sav + save.map) + 2 character.map + 1 characters.map
        Assert.Equal(21, files.Count);
        foreach (var f in files)
        {
            var json = Fixtures.DecompressedJson(f);
            Assert.True(json.AsSpan().SequenceEqual(GmJson.Write(GmJson.Parse(json))), $"round trip differs for {f}");
        }
    }

    [Fact]
    public void Long_number_text_is_preserved()
    {
        var root = (GmObject)GmJson.Parse("{ \"t\": 46316.836047453703940846025943756 }"u8);
        var n = (GmNumber)root["t"]!;
        Assert.Equal("46316.836047453703940846025943756", n.Raw);
        Assert.Equal(46316.836, n.Value, 3);
    }

    [Theory]
    [InlineData(2864, "2864.0")]
    [InlineData(-4, "-4.0")]
    [InlineData(0.5, "0.5")]
    [InlineData(0, "0.0")]
    public void GmNumber_From_formats_like_gamemaker(double v, string raw) =>
        Assert.Equal(raw, GmNumber.From(v).Raw);

    [Fact]
    public void GmString_From_escapes_quote_backslash_slash() =>
        Assert.Equal("a\\/\\\"\\\\", GmString.From("a/\"\\").Raw);

    [Fact]
    public void GmString_Value_decodes_escapes()
    {
        var root = (GmObject)GmJson.Parse("{ \"s\": \"a\\/b\\\"c\\\\d\\u0041\" }"u8);
        Assert.Equal("a/b\"c\\dA", ((GmString)root["s"]!).Value);
    }

    [Fact]
    public void GmObject_Set_replaces_in_place_and_appends_new_keys()
    {
        var obj = (GmObject)GmJson.Parse("{ \"a\": 1.0, \"b\": 2.0 }"u8);
        obj.Set("a", GmNumber.From(5));
        obj.Set("c", GmString.From("x"));
        Assert.Equal("{ \"a\": 5.0, \"b\": 2.0, \"c\": \"x\" }", Encoding.UTF8.GetString(GmJson.Write(obj)));
    }

    [Fact]
    public void GmObject_Remove_drops_entry()
    {
        var obj = (GmObject)GmJson.Parse("{ \"a\": 1.0, \"b\": 2.0 }"u8);
        Assert.True(obj.Remove("a"));
        Assert.False(obj.Remove("zz"));
        Assert.Equal("{ \"b\": 2.0 }", Encoding.UTF8.GetString(GmJson.Write(obj)));
    }

    [Fact]
    public void IsTruthy_handles_bool_and_number()
    {
        var arr = (GmArray)GmJson.Parse("[ true, 1.0, false, 0.0, -4.0 ]"u8);
        Assert.Equal(new[] { true, true, false, false, true }, arr.Items.Select(n => n.IsTruthy()));
        Assert.Equal(new[] { 1.0, 1.0, 0.0, 0.0, -4.0 }, arr.Items.Select(n => n.AsDouble()));
    }

    [Fact]
    public void DeepClone_is_independent()
    {
        var obj = (GmObject)GmJson.Parse("{ \"a\": [ 1.0 ] }"u8);
        var clone = (GmObject)obj.DeepClone();
        ((GmArray)clone["a"]!).Items.Add(GmNumber.From(2));
        Assert.Equal("{ \"a\": [ 1.0 ] }", Encoding.UTF8.GetString(GmJson.Write(obj)));
        Assert.Equal("{ \"a\": [ 1.0, 2.0 ] }", Encoding.UTF8.GetString(GmJson.Write(clone)));
    }

    [Fact]
    public void Parse_rejects_malformed_json()
    {
        Assert.Throws<FormatException>(() => GmJson.Parse("{ \"a\": }"u8));
        Assert.Throws<FormatException>(() => GmJson.Parse("[ 1.0"u8));
    }
}

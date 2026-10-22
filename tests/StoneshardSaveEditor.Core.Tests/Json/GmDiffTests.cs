using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Tests.Json;

public class GmDiffTests
{
    [Fact]
    public void Diff_reports_changed_paths()
    {
        var a = GmJson.Parse("{ \"a\": { \"b\": [ 1.0, 2.0 ] }, \"c\": \"x\", \"d\": true }"u8);
        var b = GmJson.Parse("{ \"a\": { \"b\": [ 1.0, 3.0 ] }, \"c\": \"y\", \"d\": true }"u8);

        Assert.Equal(new[] { "a.b[1]: 2.0 -> 3.0", "c: \"x\" -> \"y\"" }, GmDiff.Diff(a, b));
    }

    [Fact]
    public void Diff_reports_added_and_removed_keys_and_items()
    {
        var a = GmJson.Parse("{ \"gone\": 1.0, \"list\": [ 1.0, 2.0 ] }"u8);
        var b = GmJson.Parse("{ \"list\": [ 1.0 ], \"new\": false }"u8);

        Assert.Equal(
            new[] { "gone: 1.0 -> (missing)", "list[1]: 2.0 -> (missing)", "new: (missing) -> false" },
            GmDiff.Diff(a, b));
    }

    [Fact]
    public void Diff_reports_type_change_of_flag()
    {
        var a = GmJson.Parse("[ 1.0 ]"u8);
        var b = GmJson.Parse("[ false ]"u8);
        Assert.Equal(new[] { "[0]: 1.0 -> false" }, GmDiff.Diff(a, b));
    }

    [Fact]
    public void Identical_documents_have_no_diff() =>
        Assert.Empty(GmDiff.Diff(GmJson.Parse("{ \"a\": [ { } ] }"u8), GmJson.Parse("{ \"a\": [ { } ] }"u8)));
}

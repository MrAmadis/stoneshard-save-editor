using System.Text;

namespace StoneshardSaveEditor.Core.Json;

/// <summary>Lists every leaf that differs between two documents, as <c>path: old -> new</c>.</summary>
public static class GmDiff
{
    private const string Missing = "(missing)";
    private const int MaxValueLength = 120;

    public static IReadOnlyList<string> Diff(GmNode a, GmNode b)
    {
        var lines = new List<string>();
        Walk("", a, b, lines);
        return lines;
    }

    private static void Walk(string path, GmNode? a, GmNode? b, List<string> lines)
    {
        switch (a, b)
        {
            case (GmObject oa, GmObject ob):
                foreach (var (key, va) in oa.Entries)
                    Walk(Join(path, key), va, ob[key], lines);
                foreach (var (key, vb) in ob.Entries.Where(e => oa[e.Key] is null))
                    Walk(Join(path, key), null, vb, lines);
                break;
            case (GmArray aa, GmArray ab):
                for (var i = 0; i < Math.Max(aa.Items.Count, ab.Items.Count); i++)
                    Walk($"{path}[{i}]",
                        i < aa.Items.Count ? aa.Items[i] : null,
                        i < ab.Items.Count ? ab.Items[i] : null,
                        lines);
                break;
            default:
                var ta = Text(a);
                var tb = Text(b);
                if (ta != tb)
                    lines.Add($"{path}: {ta} -> {tb}");
                break;
        }
    }

    private static string Join(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";

    private static string Text(GmNode? node)
    {
        if (node is null)
            return Missing;
        var text = Encoding.UTF8.GetString(GmJson.Write(node));
        return text.Length <= MaxValueLength ? text : text[..MaxValueLength] + "…";
    }
}

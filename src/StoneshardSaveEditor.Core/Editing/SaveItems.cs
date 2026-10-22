using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Core.Editing;

/// <summary>Item entries found anywhere in a save: inventory, stash, traders' stock, containers.</summary>
public static class SaveItems
{
    /// <summary>Every <c>[name, props, …]</c> item entry anywhere in the save.</summary>
    public static IEnumerable<(string Name, GmObject Props)> Instances(GmNode node)
    {
        var stack = new Stack<GmNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            switch (stack.Pop())
            {
                case GmArray a:
                    if (a.Items.Count >= 2 && a.Items[0] is GmString s && a.Items[1] is GmObject p)
                        yield return (s.Value, p);
                    for (var i = a.Items.Count - 1; i >= 0; i--)
                        stack.Push(a.Items[i]);
                    break;
                case GmObject o:
                    for (var i = o.Entries.Count - 1; i >= 0; i--)
                        stack.Push(o.Entries[i].Value);
                    break;
            }
        }
    }

    /// <summary>
    /// The most charges any copy of each item has in the save. The game keeps the real maximum in code; full copies in
    /// traders' stock and loot show it (a salve's 3, confirmed by the owner).
    /// </summary>
    public static Dictionary<string, double> MaxCharges(GmNode root) =>
        Instances(root)
            .Where(i => i.Props["charge"] is GmNumber { Value: >= 1 })
            .GroupBy(i => i.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(i => ((GmNumber)i.Props["charge"]!).Value), StringComparer.Ordinal);
}

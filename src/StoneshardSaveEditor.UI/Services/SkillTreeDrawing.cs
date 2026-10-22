using System.Globalization;
using System.Text;

namespace StoneshardSaveEditor.UI.Services;

/// <summary>
/// A skill tree drawn like the game's: rows centred, and requirements drawn as bars. Like the game, the links into a
/// row are split into bars of shared requirements (the largest group of skills that all need the same skills goes
/// first) plus single lines for what is left. A skill that already has a line leaves for a second bar from its side,
/// and bars that would overlap in the same gap get lanes of their own, so separate requirements never read as one.
/// </summary>
public sealed class SkillTreeDrawing
{
    public const int SkillWidth = 82;
    public const int Gap = 22;
    public const int RowStep = 124;

    /// <summary>Lines leave a skill below its name, which sits under the icon.</summary>
    private const int LinkStart = 86;

    private const int LaneMargin = 14;

    /// <summary>How far a skill's second or later branch starts from the middle of the skill.</summary>
    private const int BranchOffset = 16;

    /// <param name="Parents">The skills needed, as object names in ordinal order.</param>
    /// <param name="Children">The skills that need them, left to right.</param>
    /// <param name="Path">SVG path data for all its lines.</param>
    /// <param name="Dots">Junctions where the lines from several needed skills meet a skill's drop.</param>
    public sealed record Link(IReadOnlyList<string> Parents, IReadOnlyList<string> Children, string Path, IReadOnlyList<(double X, double Y)> Dots);

    private SkillTreeDrawing(int width, int height, IReadOnlyList<(int Top, IReadOnlyList<SkillView> Skills)> rows, IReadOnlyList<Link> links)
    {
        Width = width;
        Height = height;
        Rows = rows;
        Links = links;
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<(int Top, IReadOnlyList<SkillView> Skills)> Rows { get; }
    public IReadOnlyList<Link> Links { get; }

    /// <summary>The drawing of a tree, or null when it has no layout (then the tab lists passives and abilities).</summary>
    public static SkillTreeDrawing? For(SkillTreeView tree)
    {
        if (tree.Skills.Count == 0 || tree.Skills.Any(s => s.Col is null || s.Row is null))
            return null;

        var rows = tree.Skills.GroupBy(s => s.Row!.Value).OrderBy(g => g.Key)
            .Select(g => (Top: g.Key * RowStep, Skills: (IReadOnlyList<SkillView>)g.OrderBy(s => s.Col).ToList()))
            .ToList();
        var widest = rows.Max(r => r.Skills.Count);
        var width = widest * SkillWidth + (widest - 1) * Gap;

        var at = new Dictionary<string, (double X, int Top)>(StringComparer.Ordinal);
        foreach (var (top, skills) in rows)
        {
            var left = (width - (skills.Count * SkillWidth + (skills.Count - 1) * Gap)) / 2.0;
            for (var i = 0; i < skills.Count; i++)
                at[skills[i].Object] = (left + i * (SkillWidth + Gap) + SkillWidth / 2.0, top);
        }

        var links = new List<Link>();
        foreach (var (top, skills) in rows)
            links.AddRange(Draw(Decompose(skills, at), at, top));

        return new SkillTreeDrawing(width, rows[^1].Top + LinkStart + 4, rows, links);
    }

    /// <summary>
    /// Splits the requirements of one row into groups: each time, the skills (up to four) whose remaining requirements
    /// share the most links (needed skills × skills needing them) become one group.
    /// </summary>
    private static List<(List<string> Parents, List<string> Children)> Decompose(IReadOnlyList<SkillView> row,
        Dictionary<string, (double X, int Top)> at)
    {
        var remaining = row.Where(s => s.Requires.Count > 0 && s.Requires.All(at.ContainsKey))
            .ToDictionary(s => s.Object, s => s.Requires.ToHashSet(StringComparer.Ordinal));
        var groups = new List<(List<string> Parents, List<string> Children)>();
        while (remaining.Values.Any(r => r.Count > 0))
        {
            var open = row.Select(s => s.Object).Where(o => remaining.GetValueOrDefault(o)?.Count > 0).ToList();
            (HashSet<string> Parents, List<string> Children) best = ([], []);
            for (var mask = 1; mask < 1 << open.Count; mask++)
            {
                var children = open.Where((_, i) => (mask & (1 << i)) != 0).ToList();
                var parents = new HashSet<string>(remaining[children[0]], StringComparer.Ordinal);
                foreach (var c in children.Skip(1))
                    parents.IntersectWith(remaining[c]);
                var score = parents.Count * children.Count;
                var bestScore = best.Parents.Count * best.Children.Count;
                if (score > bestScore || (score == bestScore && score > 0 && children.Count > best.Children.Count))
                    best = (parents, children);
            }
            foreach (var c in best.Children)
                remaining[c].ExceptWith(best.Parents);
            groups.Add((best.Parents.Order(StringComparer.Ordinal).ToList(), best.Children));
        }
        return groups;
    }

    /// <summary>The links into one row: straight lines where one skill leads to one skill below it, bars otherwise.</summary>
    private static IEnumerable<Link> Draw(List<(List<string> Parents, List<string> Children)> groups,
        Dictionary<string, (double X, int Top)> at, int childTop)
    {
        var straight = groups.Where(g => g.Parents.Count == 1 && g.Children.Count == 1 && at[g.Parents[0]].X == at[g.Children[0]].X).ToList();

        // A skill's first line leaves from its middle; further branches leave from its side, towards their bar.
        var uses = straight.ToDictionary(g => g.Parents[0], _ => 1, StringComparer.Ordinal);
        var bars = groups.Except(straight).Select(g =>
        {
            var centre = g.Parents.Concat(g.Children).Average(o => at[o].X);
            var branches = g.Parents.Select(p =>
            {
                var k = uses.GetValueOrDefault(p);
                uses[p] = k + 1;
                var side = centre >= at[p].X ? 1 : -1;
                return (Parent: p, X: at[p].X + side * BranchOffset * k);
            }).ToList();
            var xs = branches.Select(b => b.X).Concat(g.Children.Select(c => at[c].X)).ToList();
            return (Group: g, Branches: branches, Min: xs.Min(), Max: xs.Max());
        }).OrderBy(b => b.Min).ToList();

        // Greedy lanes: a bar takes the first lane where it clears the bar before it.
        var laneEnds = new List<double>();
        var lanes = new List<int>();
        foreach (var bar in bars)
        {
            var lane = laneEnds.FindIndex(end => end + LaneMargin < bar.Min);
            if (lane < 0)
            {
                laneEnds.Add(bar.Max);
                lane = laneEnds.Count - 1;
            }
            else
                laneEnds[lane] = bar.Max;
            lanes.Add(lane);
        }

        foreach (var (parents, children) in straight)
        {
            var (x, top) = at[parents[0]];
            yield return new Link(parents, children, $"M{F(x)} {top + LinkStart} V{childTop}", []);
        }

        for (var i = 0; i < bars.Count; i++)
        {
            var (group, branches, min, max) = bars[i];
            var gapTop = group.Parents.Max(p => at[p].Top) + LinkStart;
            var y = gapTop + (childTop - gapTop) * (lanes[i] + 1.0) / (laneEnds.Count + 1);
            var path = new StringBuilder();
            foreach (var (p, x) in branches)
                path.Append($"M{F(x)} {at[p].Top + LinkStart} V{F(y)} ");
            path.Append($"M{F(min)} {F(y)} H{F(max)} ");
            foreach (var c in group.Children)
                path.Append($"M{F(at[c].X)} {F(y)} V{childTop} ");
            var dots = group.Parents.Count > 1 ? group.Children.Select(c => (at[c].X, y)).ToList() : [];
            yield return new Link(group.Parents, group.Children, path.ToString().TrimEnd(), dots);
        }
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}

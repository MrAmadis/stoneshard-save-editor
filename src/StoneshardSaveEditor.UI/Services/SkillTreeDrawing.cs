using System.Globalization;
using System.Text;

namespace StoneshardSaveEditor.UI.Services;

/// <summary>
/// A skill tree drawn like the game's: rows centred, and requirements drawn as wiring (in-game findings, session D).
/// Each condition is a wire: the skills that meet it drop onto it and the skills that need only it hang from it, so
/// lines that join without a dot mean any one of them will do. Where skills need several conditions, their wires meet
/// at a dot, each from a side of its own (left, right or straight from above), and one line leaves the dot: down to
/// the skill, or to a bar over the skills that share those conditions. A skill that feeds several wires leaves for the
/// later ones from its side, and wires that would touch or cross get lanes of their own.
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

    /// <summary>Where lines start and end while lanes are chosen; lanes are numbered in between.</summary>
    private const double Above = -1000, Below = 1000;

    /// <param name="Needs">The conditions it carries, each met by any one of its skills: one for a wire, all of them for
    /// the line leaving a dot.</param>
    /// <param name="Parents">The skills in <paramref name="Needs"/>, in ordinal order.</param>
    /// <param name="Children">The skills it leads to, left to right.</param>
    /// <param name="Path">SVG path data for all its lines.</param>
    /// <param name="Dots">The dot where its conditions meet, for the line leaving one.</param>
    public sealed record Link(IReadOnlyList<string[]> Needs, IReadOnlyList<string> Parents, IReadOnlyList<string> Children, string Path,
        IReadOnlyList<(double X, double Y)> Dots);

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
        foreach (var (_, skills) in rows)
            links.AddRange(Wiring(skills, at));

        return new SkillTreeDrawing(width, rows[^1].Top + LinkStart + 4, rows, links);
    }

    /// <summary>A condition: the skills that meet it, the skills needing only it, and the dots it leads to.</summary>
    private sealed class Wire(string[] clause)
    {
        public string[] Clause { get; } = clause;
        public List<(string Parent, double X)> Branches { get; } = [];
        public List<(string Object, double X)> Children { get; } = [];
        public List<Dot> Dots { get; } = [];

        /// <summary>Where it touches its skills and dots, but the dot given.</summary>
        public IEnumerable<double> Points(Dot? except = null) =>
            Branches.Select(b => b.X).Concat(Children.Select(c => c.X)).Concat(Dots.Where(d => d != except).Select(d => d.X));

        /// <summary>One skill straight above the one skill that needs it.</summary>
        public bool Straight => Dots.Count == 0 && Branches.Count == 1 && Children.Count == 1 && Branches[0].X == Children[0].X;
    }

    /// <summary>Where the wires of the skills that need several conditions meet.</summary>
    private sealed class Dot(string[][] needs, List<(string Object, double X)> children)
    {
        public string[][] Needs { get; } = needs;
        public List<(string Object, double X)> Children { get; } = children;
        public List<Wire> Wires { get; } = [];
        public double X { get; set; } = children.Average(c => c.X);

        /// <summary>The line leaving it goes straight down into its one skill, without a bar of its own.</summary>
        public bool Direct => Children.Count == 1 && X == Children[0].X;
    }

    private readonly record struct Segment(double X1, double Y1, double X2, double Y2)
    {
        public bool Horizontal => Y1 == Y2;

        public static Segment V(double x, double y1, double y2) => new(x, y1, x, y2);
        public static Segment H(double x1, double x2, double y) => new(x1, y, x2, y);
    }

    /// <summary>The links into one row.</summary>
    private static IEnumerable<Link> Wiring(IReadOnlyList<SkillView> row, Dictionary<string, (double X, int Top)> at)
    {
        var needing = row.Where(s => s.Requires.Count > 0 && s.Requires.All(c => c.All(at.ContainsKey))).ToList();
        if (needing.Count == 0)
            return [];
        double X(string o) => at[o].X;
        var top = needing.SelectMany(s => s.Requires).SelectMany(c => c).Max(p => at[p].Top) + LinkStart;
        var bottom = at[needing[0].Object].Top;

        var wires = new Dictionary<string, Wire>(StringComparer.Ordinal);
        Wire WireFor(string[] clause)
        {
            var sorted = clause.Order(StringComparer.Ordinal).ToArray();
            var key = string.Join(' ', sorted);
            if (!wires.TryGetValue(key, out var wire))
                wires[key] = wire = new Wire(sorted);
            return wire;
        }
        foreach (var s in needing.Where(s => s.Requires.Count == 1))
            WireFor(s.Requires[0]).Children.Add((s.Object, X(s.Object)));
        var dots = needing.Where(s => s.Requires.Count > 1)
            .GroupBy(s => string.Join('+', s.Requires.Select(c => string.Join(' ', c.Order(StringComparer.Ordinal))).Order(StringComparer.Ordinal)))
            .Select(g => new Dot(g.First().Requires.ToArray(), g.Select(s => (s.Object, X(s.Object))).ToList()))
            .ToList();
        foreach (var dot in dots)
            foreach (var clause in dot.Needs)
            {
                var wire = WireFor(clause);
                wire.Dots.Add(dot);
                dot.Wires.Add(wire);
            }

        // A skill's first wire leaves from its middle (a wire to the skill right below it goes first); further wires
        // leave from its side, towards where they go.
        var uses = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var wire in wires.Values
                     .OrderBy(w => w.Children.Any(c => w.Clause.Any(p => X(p) == c.X)) ? 0 : 1)
                     .ThenBy(w => w.Children.Select(c => c.X).Concat(w.Dots.Select(d => d.X)).Min()))
        {
            var centre = wire.Clause.Select(X).Concat(wire.Children.Select(c => c.X)).Concat(wire.Dots.Select(d => d.X)).Average();
            foreach (var p in wire.Clause)
            {
                var k = uses.GetValueOrDefault(p);
                uses[p] = k + 1;
                wire.Branches.Add((p, X(p) + (centre >= X(p) ? 1 : -1) * BranchOffset * k));
            }
        }
        foreach (var dot in dots)
            dot.X = PlaceDot(dot);

        // Lanes: straight lines need none; each group of wires meeting at dots shares one, and a dot's bar goes below it.
        var placed = new List<Segment>();
        var lanes = new List<double>();
        var lane = new Dictionary<Wire, double>();
        var fan = new Dictionary<Dot, double>();
        foreach (var wire in wires.Values.Where(w => w.Straight))
            placed.AddRange(Lines(wire, 0, Above, Below));
        foreach (var group in Groups(wires.Values.Where(w => !w.Straight))
                     .OrderBy(g => g.Any(w => w.Dots.Count > 0) ? 0 : 1).ThenBy(g => g.SelectMany(w => w.Points()).Min()))
        {
            var direct = group.SelectMany(w => w.Dots).Distinct().Where(d => d.Direct).ToList();
            var y = Place(c => group.SelectMany(w => Lines(w, c, Above, Below)).Concat(direct.SelectMany(d => Leaving(d, c, c, Below))),
                lanes, placed);
            foreach (var wire in group)
                lane[wire] = y;
        }
        foreach (var dot in dots.Where(d => !d.Direct))
        {
            var y = lane[dot.Wires[0]];
            fan[dot] = Place(c => Leaving(dot, y + 0.01, c, Below), lanes, placed, below: y);
        }

        var order = lanes.Distinct().Order().ToList();
        double Y(double l) => top + (bottom - top) * (order.IndexOf(l) + 1.0) / (order.Count + 1);

        var links = wires.Values.OrderBy(w => w.Points().Min()).Select(w => new Link(
            [w.Clause], w.Clause,
            w.Children.Concat(w.Dots.SelectMany(d => d.Children)).OrderBy(c => c.X).Select(c => c.Object).Distinct().ToList(),
            Path(w.Straight ? [Segment.V(w.Branches[0].X, top, bottom)] : Lines(w, Y(lane[w]), top, bottom)),
            [])).ToList();
        links.AddRange(dots.OrderBy(d => d.X).Select(d =>
        {
            var y = Y(lane[d.Wires[0]]);
            return new Link(d.Needs, d.Needs.SelectMany(c => c).Distinct().Order(StringComparer.Ordinal).ToList(),
                d.Children.Select(c => c.Object).ToList(), Path(Leaving(d, y, d.Direct ? y : Y(fan[d]), bottom)), [(d.X, y)]);
        }));
        return links;
    }

    /// <summary>
    /// Where a dot goes: as near as it can be to the middle of its skills while each of its wires reaches it from a
    /// side of its own, so wires only ever meet at the dot.
    /// </summary>
    private static double PlaceDot(Dot dot)
    {
        var preferred = dot.Children.Average(c => c.X);
        var points = dot.Wires.SelectMany(w => w.Points(dot)).Concat(dot.Children.Select(c => c.X)).Append(preferred).Distinct().Order().ToList();
        return points.Concat(points.Zip(points.Skip(1), (a, b) => (a + b) / 2))
            .Where(x => Reaches(dot, x))
            .OrderBy(x => Math.Abs(x - preferred)).ThenBy(x => x)
            .DefaultIfEmpty(preferred)
            .First();
    }

    private static bool Reaches(Dot dot, double x)
    {
        var sides = dot.Wires.Select(w =>
        {
            var points = w.Points(dot).ToList();
            if (points.All(p => p < x))
                return -1;
            if (points.All(p => p > x))
                return 1;
            return points.Count == 1 && points[0] == x ? 0 : (int?)null; // its one skill right above the dot
        }).ToList();
        return sides.All(s => s is not null) && sides.Distinct().Count() == sides.Count;
    }

    /// <summary>Wires that meet at a dot, together.</summary>
    private static List<List<Wire>> Groups(IEnumerable<Wire> wires)
    {
        var groups = new List<List<Wire>>();
        foreach (var wire in wires)
        {
            var joined = groups.Where(g => g.Any(o => o.Dots.Intersect(wire.Dots).Any())).ToList();
            groups.RemoveAll(joined.Contains);
            groups.Add([.. joined.SelectMany(g => g), wire]);
        }
        return groups;
    }

    /// <summary>
    /// The lane with the fewest clashes with what is already drawn: one in use, or failing that a new one above, between
    /// or below them (below <paramref name="below"/> when given).
    /// </summary>
    private static double Place(Func<double, IEnumerable<Segment>> lines, List<double> lanes, List<Segment> placed, double? below = null)
    {
        var used = lanes.Distinct().Order().ToList();
        var fresh = used.Count == 0 ? [0] : used.Zip(used.Skip(1), (a, b) => (a + b) / 2).Append(used[0] - 1).Append(used[^1] + 1).ToList();
        var best = used.Concat(fresh)
            .Where(c => below is null || c > below)
            .OrderBy(c => lines(c).Sum(s => placed.Count(t => Clash(s, t))))
            .ThenBy(c => used.Contains(c) ? 0 : 1)
            .ThenBy(c => c)
            .First();
        placed.AddRange(lines(best));
        lanes.Add(best);
        return best;
    }

    /// <summary>Lines of different links that would touch, overlap or cross.</summary>
    private static bool Clash(Segment s, Segment t)
    {
        if (s.Horizontal && t.Horizontal)
            return s.Y1 == t.Y1 && s.X1 <= t.X2 + LaneMargin && t.X1 <= s.X2 + LaneMargin;
        if (!s.Horizontal && !t.Horizontal)
            return Math.Abs(s.X1 - t.X1) < 6 && Math.Max(s.Y1, t.Y1) < Math.Min(s.Y2, t.Y2);
        var (h, v) = s.Horizontal ? (s, t) : (t, s);
        return v.X1 >= h.X1 - 6 && v.X1 <= h.X2 + 6 && h.Y1 >= v.Y1 && h.Y1 <= v.Y2;
    }

    /// <summary>A wire on lane <paramref name="y"/>: its skills drop onto it, and the skills needing only it hang from it.</summary>
    private static IEnumerable<Segment> Lines(Wire wire, double y, double top, double bottom)
    {
        foreach (var (_, x) in wire.Branches)
            yield return Segment.V(x, top, y);
        var points = wire.Points().ToList();
        if (points.Max() > points.Min())
            yield return Segment.H(points.Min(), points.Max(), y);
        foreach (var (_, x) in wire.Children)
            yield return Segment.V(x, y, bottom);
    }

    /// <summary>The line leaving a dot on lane <paramref name="y"/>: into its skill, or down to a bar on lane <paramref name="fan"/>.</summary>
    private static IEnumerable<Segment> Leaving(Dot dot, double y, double fan, double bottom)
    {
        if (dot.Direct)
        {
            yield return Segment.V(dot.X, y, bottom);
            yield break;
        }
        yield return Segment.V(dot.X, y, fan);
        var xs = dot.Children.Select(c => c.X).Append(dot.X).ToList();
        if (xs.Max() > xs.Min())
            yield return Segment.H(xs.Min(), xs.Max(), fan);
        foreach (var (_, x) in dot.Children)
            yield return Segment.V(x, fan, bottom);
    }

    private static string Path(IEnumerable<Segment> segments) =>
        string.Join(' ', segments.Select(s => $"M{F(s.X1)} {F(s.Y1)} {(s.Horizontal ? $"H{F(s.X2)}" : $"V{F(s.Y2)}")}"));

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}

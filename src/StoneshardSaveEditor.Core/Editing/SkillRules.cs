namespace StoneshardSaveEditor.Core.Editing;

/// <summary>
/// The game's skill prerequisites, used by "Follow game rules".
/// <para>
/// Conclusion: a skill needs <b>all</b> the skills it requires. In-game finding A3 showed that Greataxes' second tier
/// stays locked until all three first-tier skills are learned, and the trees draw those as one shared bar. Every line
/// into a skill is read the same way, so a skill with lines from two parents needs both. First-tier skills (no
/// requirements) are available while their tree is open; the game recomputes the locks inside open trees on load.
/// </para>
/// </summary>
public sealed class SkillRules(IReadOnlyDictionary<string, string[]> requires, Func<string, IReadOnlyList<string>> firstTierOfTreeOf)
{
    public bool CanLearn(string obj, IReadOnlyList<SkillState> state)
    {
        var at = Index(state);
        if (at.GetValueOrDefault(obj) is not { Learned: false } skill)
            return false;
        var needs = Requires(obj);
        return needs.Length == 0 ? !skill.Locked : needs.All(r => at.GetValueOrDefault(r)?.Learned == true);
    }

    /// <summary>Locked skills that learning <paramref name="obj"/> makes available.</summary>
    public IReadOnlyList<string> ToUnlockAfterLearn(string obj, IReadOnlyList<SkillState> state)
    {
        var at = Index(state);
        bool Learned(string o) => o == obj || at.GetValueOrDefault(o)?.Learned == true;
        return Dependents(obj)
            .Where(d => at.GetValueOrDefault(d) is { Learned: false, Locked: true } && Requires(d).All(Learned))
            .ToList();
    }

    /// <summary>Available skills that unlearning <paramref name="obj"/> locks again.</summary>
    public IReadOnlyList<string> ToRelockAfterUnlearn(string obj, IReadOnlyList<SkillState> state)
    {
        var at = Index(state);
        return Dependents(obj).Where(d => at.GetValueOrDefault(d) is { Learned: false, Locked: false }).ToList();
    }

    /// <summary>A learned skill that needs <paramref name="obj"/>, which has to be unlearned first.</summary>
    public string? BlockingDependent(string obj, IReadOnlyList<SkillState> state)
    {
        var at = Index(state);
        return Dependents(obj).FirstOrDefault(d => at.GetValueOrDefault(d)?.Learned == true);
    }

    /// <summary>After a respec only first-tier skills stay available: every other unlearned skill locks again.</summary>
    public IReadOnlyList<string> ToRelockAfterRespec(IReadOnlyList<SkillState> state) =>
        state.Where(s => s is { Learned: false, Locked: false } && requires.ContainsKey(s.Object) && !firstTierOfTreeOf(s.Object).Contains(s.Object))
            .Select(s => s.Object)
            .ToList();

    /// <summary>What <paramref name="obj"/> needs; empty for first-tier skills and skills outside the known trees.</summary>
    public IReadOnlyList<string> RequirementsOf(string obj) => Requires(obj);

    private string[] Requires(string obj) => requires.GetValueOrDefault(obj) ?? [];

    private IEnumerable<string> Dependents(string obj) => requires.Where(r => r.Value.Contains(obj)).Select(r => r.Key);

    private static Dictionary<string, SkillState> Index(IReadOnlyList<SkillState> state) => state.ToDictionary(s => s.Object);
}

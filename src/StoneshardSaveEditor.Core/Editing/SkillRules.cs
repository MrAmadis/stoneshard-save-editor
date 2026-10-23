namespace StoneshardSaveEditor.Core.Editing;

/// <summary>
/// The game's skill prerequisites, used by "Follow game rules". A skill is learnable when two gates are open:
/// <list type="bullet">
/// <item>its tier: the save's lock flag, cleared by reading that tree's treatise volume (session B: Pyromancy II–IV each
/// opened one row; session D: Shield Bash stayed shut with both its skills learned until the second volume was read);</item>
/// <item>its lines: every skill it requires is learned (A3: Greataxes' second tier needs all three first-tier skills,
/// drawn as one shared bar). Every line into a skill is read the same way, so a skill with lines from two parents needs both.</item>
/// </list>
/// Learning or unlearning never changes the locks; only books do.
/// </summary>
public sealed class SkillRules(IReadOnlyDictionary<string, string[]> requires, Func<string, IReadOnlyList<string>> firstTierOfTreeOf)
{
    public bool CanLearn(string obj, IReadOnlyList<SkillState> state)
    {
        var at = Index(state);
        return at.GetValueOrDefault(obj) is { Learned: false, Locked: false }
               && Requires(obj).All(r => at.GetValueOrDefault(r)?.Learned == true);
    }

    /// <summary>A learned skill that needs <paramref name="obj"/>, which has to be unlearned first.</summary>
    public string? BlockingDependent(string obj, IReadOnlyList<SkillState> state)
    {
        var at = Index(state);
        return Dependents(obj).FirstOrDefault(d => at.GetValueOrDefault(d)?.Learned == true);
    }

    /// <summary>What <paramref name="obj"/> needs; empty for the first tier and skills outside the known trees.</summary>
    public IReadOnlyList<string> RequirementsOf(string obj) => Requires(obj);

    /// <summary>Whether <paramref name="obj"/> is in its tree's first tier (needs nothing).</summary>
    public bool IsFirstTier(string obj) => firstTierOfTreeOf(obj).Contains(obj);

    private string[] Requires(string obj) => requires.GetValueOrDefault(obj) ?? [];

    private IEnumerable<string> Dependents(string obj) => requires.Where(r => r.Value.Contains(obj)).Select(r => r.Key);

    private static Dictionary<string, SkillState> Index(IReadOnlyList<SkillState> state) => state.ToDictionary(s => s.Object);
}

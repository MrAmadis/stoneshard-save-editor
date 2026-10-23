namespace StoneshardSaveEditor.Core.Editing;

/// <summary>
/// The game's skill prerequisites, used by "Follow game rules". A skill is learnable when two gates are open:
/// <list type="bullet">
/// <item>its tier: the save's lock flag, cleared by reading that tree's treatise volume (session B: Pyromancy II–IV each
/// opened one row; session D: Shield Bash stayed shut with its skills learned until the second volume was read);</item>
/// <item>its lines (sessions D and E): a skill with a dot on its line needs every skill whose line runs into the dot
/// (Regroup needed all three; Reprisal all four of Axes' first row); any other skill needs any one of the skills whose
/// lines run into it (Shield Bash opened with Breakthrough alone).</item>
/// </list>
/// So a skill's requirement is a list of conditions that must all hold, each satisfied by any one of its skills.
/// Learning or unlearning never changes the locks; only books do.
/// </summary>
public sealed class SkillRules(IReadOnlyDictionary<string, string[][]> requires, Func<string, IReadOnlyList<string>> firstTierOfTreeOf)
{
    public bool CanLearn(string obj, IReadOnlyList<SkillState> state)
    {
        var at = Index(state);
        return at.GetValueOrDefault(obj) is { Learned: false, Locked: false } && Satisfied(obj, o => at.GetValueOrDefault(o)?.Learned == true);
    }

    /// <summary>A learned skill that would lose its last way in if <paramref name="obj"/> were unlearned.</summary>
    public string? BlockingDependent(string obj, IReadOnlyList<SkillState> state)
    {
        var at = Index(state);
        bool LearnedWithout(string o) => o != obj && at.GetValueOrDefault(o)?.Learned == true;
        return requires
            .Where(r => r.Value.Any(c => c.Contains(obj)) && at.GetValueOrDefault(r.Key)?.Learned == true)
            .Select(r => r.Key)
            .FirstOrDefault(d => !Satisfied(d, LearnedWithout));
    }

    /// <summary>The conditions <paramref name="obj"/> needs, each met by any one of its skills; empty for the first tier.</summary>
    public IReadOnlyList<string[]> RequirementsOf(string obj) => Requires(obj);

    /// <summary>Whether <paramref name="obj"/> is in its tree's first tier (needs nothing).</summary>
    public bool IsFirstTier(string obj) => firstTierOfTreeOf(obj).Contains(obj);

    private bool Satisfied(string obj, Func<string, bool> learned) => Requires(obj).All(condition => condition.Any(learned));

    private string[][] Requires(string obj) => requires.GetValueOrDefault(obj) ?? [];

    private static Dictionary<string, SkillState> Index(IReadOnlyList<SkillState> state) => state.ToDictionary(s => s.Object);
}

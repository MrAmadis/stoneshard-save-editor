using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Tests.Editing;

public class SkillRulesTests
{
    // Synthetic tree: A (tier 1) → B, A → C, B → D; E needs both B and C (a shared bar in the game).
    private static readonly SkillRules Rules = new(
        new Dictionary<string, string[]> { ["A"] = [], ["B"] = ["A"], ["C"] = ["A"], ["D"] = ["B"], ["E"] = ["B", "C"] },
        _ => ["A"]);

    private static List<SkillState> State(string learned, string locked) =>
        "ABCDE".Select(c => new SkillState(c.ToString(), learned.Contains(c), locked.Contains(c))).ToList();

    [Fact]
    public void Learning_A_unlocks_B_and_C()
    {
        var state = State(learned: "", locked: "BCDE");

        Assert.True(Rules.CanLearn("A", state));
        Assert.Equal(new[] { "B", "C" }, Rules.ToUnlockAfterLearn("A", state).Order());
    }

    [Fact]
    public void Locked_D_cannot_be_learned()
    {
        var state = State(learned: "A", locked: "DE");

        Assert.False(Rules.CanLearn("D", state));
        Assert.True(Rules.CanLearn("B", state));
        Assert.False(Rules.CanLearn("A", state));
    }

    [Fact]
    public void A_skill_needing_two_unlocks_only_when_both_are_learned()
    {
        Assert.Equal(new[] { "D" }, Rules.ToUnlockAfterLearn("B", State(learned: "A", locked: "DE")));
        Assert.Equal(new[] { "D", "E" }, Rules.ToUnlockAfterLearn("B", State(learned: "AC", locked: "DE")).Order());
    }

    [Fact]
    public void Unlearning_A_is_blocked_while_B_is_learned()
    {
        var state = State(learned: "AB", locked: "E");

        Assert.Equal("B", Rules.BlockingDependent("A", state));
        Assert.Null(Rules.BlockingDependent("B", state));
    }

    [Fact]
    public void Unlearning_A_relocks_unlearned_B_and_C()
    {
        var state = State(learned: "A", locked: "DE");

        Assert.Null(Rules.BlockingDependent("A", state));
        Assert.Equal(new[] { "B", "C" }, Rules.ToRelockAfterUnlearn("A", state).Order());
    }

    [Fact]
    public void After_respec_only_first_tier_stays_unlocked()
    {
        var state = State(learned: "", locked: "E");

        Assert.Equal(new[] { "B", "C", "D" }, Rules.ToRelockAfterRespec(state).Order());
    }

    /// <summary>Velmir's Greataxes: Hooking Chop, Shieldbreaker and Dismember open the second tier (in-game finding A3).</summary>
    private static readonly SkillRules Greataxes = new(
        new Dictionary<string, string[]>
        {
            ["o_skill_wide_cut_ico"] = [], ["o_pass_skill_shieldbreaker"] = [], ["o_skill_dismember_ico"] = [],
            ["o_pass_skill_fatal_hit"] = ["o_skill_wide_cut_ico", "o_pass_skill_shieldbreaker", "o_skill_dismember_ico"],
        },
        _ => ["o_skill_wide_cut_ico", "o_pass_skill_shieldbreaker", "o_skill_dismember_ico"]);

    private static string Sp(SaveSession s) => ((GmNumber)((GmObject)s.Root["characterDataMap"]!)["SP"]!).Raw;

    private static SkillState Skill(SkillsEditor skills, string obj) => skills.Read().Single(x => x.Object == obj);

    [Fact]
    public void With_rules_learning_spends_a_point_and_unlocking_follows()
    {
        var session = Sessions.OpenVelmir();
        var skills = new SkillsEditor(session);
        skills.Unlearn("o_skill_wide_cut_ico", refundPoint: false);
        skills.Lock(["o_pass_skill_fatal_hit"]);
        new CharacterEditor(session).Set(CharacterField.AbilityPoints, 1);

        skills.Learn("o_skill_wide_cut_ico", Greataxes);

        Assert.Equal("0.0", Sp(session));
        Assert.False(Skill(skills, "o_pass_skill_fatal_hit").Locked);
        Assert.Throws<EditRejectedException>(() => skills.Learn("o_pass_skill_fatal_hit", Greataxes));
    }

    [Fact]
    public void With_rules_unlearning_is_blocked_by_a_learned_dependent_then_relocks()
    {
        var session = Sessions.OpenVelmir();
        var skills = new SkillsEditor(session);
        skills.Learn("o_pass_skill_fatal_hit", spendPoint: false);

        var blocked = Assert.Throws<EditRejectedException>(() => skills.Unlearn("o_skill_wide_cut_ico", Greataxes));
        Assert.Equal("Unlearn o_pass_skill_fatal_hit first", blocked.Message);

        skills.Unlearn("o_pass_skill_fatal_hit", Greataxes);
        skills.Unlearn("o_skill_wide_cut_ico", Greataxes);

        Assert.True(Skill(skills, "o_pass_skill_fatal_hit").Locked);
        Assert.Equal("2.0", Sp(session));
    }

    [Fact]
    public void Without_rules_learn_and_unlearn_only_toggle()
    {
        var session = Sessions.OpenVelmir();
        var skills = new SkillsEditor(session);

        skills.Learn("o_pass_skill_fatal_hit", rules: null);
        skills.Unlearn("o_skill_wide_cut_ico", rules: null);

        Assert.True(Skill(skills, "o_pass_skill_fatal_hit").Learned);
        Assert.False(Skill(skills, "o_skill_wide_cut_ico").Learned);
        Assert.Equal("0.0", Sp(session));
    }
}

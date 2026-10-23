using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Tests.Editing;

public class SkillRulesTests
{
    // Synthetic tree: A (tier 1) → B, A → C, B → D; E needs both B and C (a shared bar in the game).
    // A skill's lock is its tier's book (session D); its lines are the skills it needs.
    private static readonly SkillRules Rules = new(
        new Dictionary<string, string[]> { ["A"] = [], ["B"] = ["A"], ["C"] = ["A"], ["D"] = ["B"], ["E"] = ["B", "C"] },
        _ => ["A"]);

    private static List<SkillState> State(string learned, string locked) =>
        "ABCDE".Select(c => new SkillState(c.ToString(), learned.Contains(c), locked.Contains(c))).ToList();

    [Fact]
    public void A_first_tier_skill_is_learnable_while_its_tree_is_open()
    {
        Assert.True(Rules.CanLearn("A", State(learned: "", locked: "BCDE")));
        Assert.False(Rules.CanLearn("A", State(learned: "", locked: "ABCDE")));
        Assert.False(Rules.CanLearn("A", State(learned: "A", locked: "")));
    }

    [Fact]
    public void A_skill_needs_its_tier_opened_by_the_book_and_its_lines_learned()
    {
        // Session D: Shield Bash stayed shut with both its skills learned until the second volume was read.
        Assert.False(Rules.CanLearn("B", State(learned: "A", locked: "BCDE")));
        Assert.True(Rules.CanLearn("B", State(learned: "A", locked: "DE")));
        Assert.False(Rules.CanLearn("D", State(learned: "A", locked: "")));
    }

    [Fact]
    public void A_skill_needing_two_waits_for_both()
    {
        Assert.False(Rules.CanLearn("E", State(learned: "AB", locked: "")));
        Assert.True(Rules.CanLearn("E", State(learned: "ABC", locked: "")));
    }

    [Fact]
    public void Unlearning_A_is_blocked_while_B_is_learned()
    {
        var state = State(learned: "AB", locked: "E");

        Assert.Equal("B", Rules.BlockingDependent("A", state));
        Assert.Null(Rules.BlockingDependent("B", state));
    }

    [Fact]
    public void Requirements_are_listed_for_tooltips()
    {
        Assert.Equal(new[] { "B", "C" }, Rules.RequirementsOf("E"));
        Assert.Empty(Rules.RequirementsOf("A"));
    }

    /// <summary>Velmir's Greataxes: Hooking Chop, Shieldbreaker and Dismember lead to the second tier (in-game finding A3).</summary>
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
    public void With_rules_learning_spends_a_point_and_opens_nothing()
    {
        var session = Sessions.OpenVelmir();
        var skills = new SkillsEditor(session);
        skills.Unlearn("o_skill_wide_cut_ico", refundPoint: false);
        new CharacterEditor(session).Set(CharacterField.AbilityPoints, 2);

        skills.Learn("o_skill_wide_cut_ico", Greataxes);

        Assert.Equal("1.0", Sp(session));
        Assert.True(Skill(skills, "o_pass_skill_fatal_hit").Locked); // Velmir hasn't read Greataxes II
        Assert.Throws<EditRejectedException>(() => skills.Learn("o_pass_skill_fatal_hit", Greataxes));

        skills.Unlock(["o_pass_skill_fatal_hit"]); // as reading the second volume does
        skills.Learn("o_pass_skill_fatal_hit", Greataxes);
        Assert.Equal("0.0", Sp(session));
    }

    [Fact]
    public void With_rules_unlearning_is_blocked_by_a_learned_dependent_and_keeps_the_locks()
    {
        var session = Sessions.OpenVelmir();
        var skills = new SkillsEditor(session);
        skills.Learn("o_pass_skill_fatal_hit", spendPoint: false);

        var blocked = Assert.Throws<EditRejectedException>(() => skills.Unlearn("o_skill_wide_cut_ico", Greataxes));
        Assert.Equal("Unlearn o_pass_skill_fatal_hit first", blocked.Message);

        skills.Unlearn("o_pass_skill_fatal_hit", Greataxes);
        skills.Unlearn("o_skill_wide_cut_ico", Greataxes);

        Assert.False(Skill(skills, "o_pass_skill_fatal_hit").Locked); // learning made it unlocked; unlearning leaves the book's lock alone
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

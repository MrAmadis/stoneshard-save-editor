using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Tests.Editing;

public class SkillRulesTests
{
    // Synthetic tree: A (tier 1) → B and C; D needs B; E needs B and C (lines meeting at a dot);
    // F needs B or C (lines joining without a dot). A skill's lock is its tier's book.
    private static readonly SkillRules Rules = new(
        new Dictionary<string, string[][]>
        {
            ["A"] = [], ["B"] = [["A"]], ["C"] = [["A"]], ["D"] = [["B"]], ["E"] = [["B"], ["C"]], ["F"] = [["B", "C"]],
        },
        _ => ["A"]);

    private static List<SkillState> State(string learned, string locked = "") =>
        "ABCDEF".Select(c => new SkillState(c.ToString(), learned.Contains(c), locked.Contains(c))).ToList();

    [Fact]
    public void A_first_tier_skill_is_learnable_while_its_tree_is_open()
    {
        Assert.True(Rules.CanLearn("A", State(learned: "", locked: "BCDEF")));
        Assert.False(Rules.CanLearn("A", State(learned: "", locked: "ABCDEF")));
        Assert.False(Rules.CanLearn("A", State(learned: "A")));
    }

    [Fact]
    public void A_skill_needs_its_tier_opened_by_the_book_and_its_lines_learned()
    {
        // Session D: Shield Bash stayed shut with its skills learned until the second volume was read.
        Assert.False(Rules.CanLearn("B", State(learned: "A", locked: "BCDEF")));
        Assert.True(Rules.CanLearn("B", State(learned: "A")));
        Assert.False(Rules.CanLearn("D", State(learned: "A")));
    }

    [Fact]
    public void Lines_meeting_at_a_dot_are_all_needed()
    {
        // Session D: Regroup needed all three skills feeding its dot; Fatal Strike all three of Greataxes' first row.
        Assert.False(Rules.CanLearn("E", State(learned: "AB")));
        Assert.True(Rules.CanLearn("E", State(learned: "ABC")));
    }

    [Fact]
    public void Lines_joining_without_a_dot_need_any_one()
    {
        // Session D: Shield Bash opened with Breakthrough alone; Elusiveness with Dash alone.
        Assert.True(Rules.CanLearn("F", State(learned: "AB")));
        Assert.True(Rules.CanLearn("F", State(learned: "AC")));
        Assert.False(Rules.CanLearn("F", State(learned: "A")));
    }

    [Fact]
    public void Unlearning_is_blocked_only_when_a_learned_skill_would_lose_its_last_way_in()
    {
        Assert.Equal("D", Rules.BlockingDependent("B", State(learned: "ABD")));
        Assert.Equal("E", Rules.BlockingDependent("C", State(learned: "ABCE")));
        Assert.Null(Rules.BlockingDependent("B", State(learned: "ABCF"))); // F still has C
        Assert.Equal("F", Rules.BlockingDependent("B", State(learned: "ABF")));
    }

    [Fact]
    public void Requirements_are_listed_for_tooltips()
    {
        Assert.Equal(new[] { new[] { "B" }, ["C"] }, Rules.RequirementsOf("E"));
        Assert.Equal(new[] { new[] { "B", "C" } }, Rules.RequirementsOf("F"));
        Assert.Empty(Rules.RequirementsOf("A"));
    }

    /// <summary>Velmir's Greataxes: Hooking Chop, Shieldbreaker and Dismember all lead to the second tier (A3, session D).</summary>
    private static readonly SkillRules Greataxes = new(
        new Dictionary<string, string[][]>
        {
            ["o_skill_wide_cut_ico"] = [], ["o_pass_skill_shieldbreaker"] = [], ["o_skill_dismember_ico"] = [],
            ["o_pass_skill_fatal_hit"] = [["o_skill_wide_cut_ico"], ["o_pass_skill_shieldbreaker"], ["o_skill_dismember_ico"]],
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

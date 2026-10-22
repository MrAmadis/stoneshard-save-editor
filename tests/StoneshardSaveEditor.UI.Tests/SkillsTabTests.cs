using Bunit;
using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.UI.Screens;
using StoneshardSaveEditor.UI.Tabs;

namespace StoneshardSaveEditor.Tests;

public class SkillsTabTests
{
    private static (EditorServiceTests.Harness H, BunitContext Ctx, IRenderedComponent<Editor> Cut) OpenSkills()
    {
        var h = EditorServiceTests.Start(gameDir: Fixtures.GameDir);
        h.Editor.Open("character_2", "exitsave_1");
        var ctx = ShellTests.Context(h);
        var cut = ctx.Render<Editor>();
        cut.FindAll("button.tab").Single(b => b.TextContent == "Skills").Click();
        return (h, ctx, cut);
    }

    private static AngleSharp.Dom.IElement Skill(IRenderedComponent<Editor> cut, string name) =>
        cut.FindAll(".skill").Single(s => s.QuerySelector(".nm")!.TextContent == name);

    [Fact]
    public void Greataxes_shows_Hooking_Chop_learned_and_clicking_toggles_it()
    {
        var (h, ctx, cut) = OpenSkills();
        using var _ctx = ctx;

        cut.Find(".tree-list [data-tree='2haxe']").Click();
        Assert.Contains("learned", Skill(cut, "Hooking Chop").ClassList);

        Skill(cut, "Hooking Chop").Click();
        var greataxes = h.Editor.Current.Trees.Single(t => t.Id == "2haxe");
        Assert.False(greataxes.Skills.Single(s => s.DisplayName == "Hooking Chop").Learned);
        Assert.Contains("available", Skill(cut, "Hooking Chop").ClassList);

        Skill(cut, "Hooking Chop").Click();
        Assert.True(h.Editor.Current.Trees.Single(t => t.Id == "2haxe").Skills.Single(s => s.DisplayName == "Hooking Chop").Learned);
    }

    [Fact]
    public void Respec_all_asks_to_confirm_then_refunds()
    {
        var (h, ctx, cut) = OpenSkills();
        using var _ctx = ctx;
        var points = h.Editor.Current.Character[CharacterField.AbilityPoints];
        var learned = h.Editor.Current.Trees.Sum(t => t.LearnedCount);

        cut.Find("button.respec").Click();
        Assert.Contains("Unlearn every skill you picked and refund the points?", cut.Markup);
        Assert.Equal(0, h.Editor.Current.ChangeCount);

        cut.Find("button.confirm-respec").Click();
        // Innate skills (every character starts with them) stay; each bought skill gives its point back.
        var unlearned = learned - h.Editor.Current.Trees.Sum(t => t.LearnedCount);
        Assert.True(unlearned > 0);
        Assert.Equal(points + unlearned, h.Editor.Current.Character[CharacterField.AbilityPoints]);
        Assert.DoesNotContain("Unlearn every skill you picked", cut.Markup);
    }

    [Fact]
    public void Closed_tree_offers_Unlock_tree_which_opens_it()
    {
        var (h, ctx, cut) = OpenSkills();
        using var _ctx = ctx;
        var closed = h.Editor.Current.Trees.First(t => !t.Open && t.CanUnlock);

        cut.Find($".tree-list [data-tree='{closed.Id}']").Click();
        Assert.Contains("🔒", cut.Find($".tree-list [data-tree='{closed.Id}']").TextContent);
        cut.Find("button.unlock-tree").Click();

        Assert.True(h.Editor.Current.Trees.Single(t => t.Id == closed.Id).Open);
        Assert.Empty(cut.FindAll("button.unlock-tree"));
        Assert.NotEmpty(cut.FindAll(".skill.available"));
    }

    [Fact]
    public void Points_are_editable_in_the_top_bar()
    {
        var (h, ctx, cut) = OpenSkills();
        using var _ctx = ctx;

        cut.Find(".skills-bar [data-field='AbilityPoints'] input").Change("7");

        Assert.Equal(7, h.Editor.Current.Character[CharacterField.AbilityPoints]);
    }

    [Fact]
    public void Trees_follow_the_game_layout_and_rules_are_on()
    {
        var (_, ctx, cut) = OpenSkills();
        using var _ctx = ctx;

        cut.Find(".tree-list [data-tree='2haxe']").Click();

        var rows = cut.FindAll(".tree-view .skill-row");
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "Hooking Chop", "Shieldbreaker", "Dismember" }, rows[0].QuerySelectorAll(".nm").Select(n => n.TextContent));
        var toggle = cut.Find("input.rules");
        Assert.False(toggle.HasAttribute("disabled"));
        Assert.True(toggle.HasAttribute("checked"));
    }

    [Fact]
    public void Lines_link_each_skill_to_what_it_needs()
    {
        var (_, ctx, cut) = OpenSkills();
        using var _ctx = ctx;

        cut.Find(".tree-list [data-tree='2haxe']").Click();

        // One shared bar from the three learned first-tier skills to the whole second row, drawn as met.
        var second = cut.Find(".links g[data-children='o_skill_rampage_ico o_pass_skill_fatal_hit o_pass_skill_finish_him o_skill_reign_in_blood_ico']");
        Assert.Equal("o_pass_skill_shieldbreaker o_skill_dismember_ico o_skill_wide_cut_ico", second.GetAttribute("data-parents"));
        Assert.Contains("met", second.ClassList);
        var makeSpace = cut.Find(".links g[data-children='o_skill_killing_swing_ico']");
        Assert.Equal("o_pass_skill_fatal_hit o_pass_skill_finish_him", makeSpace.GetAttribute("data-parents"));
        Assert.DoesNotContain("met", makeSpace.ClassList);
        Assert.Equal(4, cut.FindAll(".links g").Count); // the bar above, Make Space, and the two single lines

        cut.Find(".skill-cell[data-skill='o_skill_killing_swing_ico']").MouseOver();
        Assert.Contains("hi", cut.Find(".links g[data-children='o_skill_killing_swing_ico']").ClassList);
    }

    [Fact]
    public void Shared_requirements_get_one_bar_and_the_rest_single_lines()
    {
        var (h, ctx, cut) = OpenSkills();
        using var _ctx = ctx;
        var staves = h.Editor.Current.Trees.Single(t => t.Id == "staff");
        string Obj(string name) => staves.Skills.Single(s => s.DisplayName == name).Object;

        cut.Find(".tree-list [data-tree='staff']").Click();

        // Like the game: Hail of Blows and Step Aside! feed all three skills below through one bar; Destabilizing Hits
        // and Battle Trance each add a single line.
        var shared = cut.Find($".links g[data-parents='{string.Join(' ', new[] { Obj("Hail of Blows"), Obj("Step Aside!") }.Order(StringComparer.Ordinal))}']");
        Assert.Equal(string.Join(' ', Obj("Now or Never"), Obj("Unwavering Stance"), Obj("Triumph")), shared.GetAttribute("data-children"));
        Assert.Equal(Obj("Now or Never"), cut.Find($".links g[data-parents='{Obj("Destabilizing Hits")}']").GetAttribute("data-children"));
        Assert.Equal(Obj("Triumph"), cut.Find($".links g[data-parents='{Obj("Battle Trance")}']").GetAttribute("data-children"));
    }

    [Fact]
    public void With_rules_a_locked_skill_says_what_it_needs()
    {
        var (_, ctx, cut) = OpenSkills();
        using var _ctx = ctx;

        cut.Find(".tree-list [data-tree='2haxe']").Click();
        var makeSpace = cut.Find(".skill[data-skill='o_skill_killing_swing_ico']");

        Assert.True(makeSpace.HasAttribute("disabled"));
        Assert.Equal("Locked: learn Fatal Strike and Finish 'Em! first", makeSpace.GetAttribute("title"));
    }

    [Fact]
    public void With_rules_learning_costs_a_point_and_needed_skills_stay()
    {
        var (h, ctx, cut) = OpenSkills();
        using var _ctx = ctx;
        cut.Find(".skills-bar [data-field='AbilityPoints'] input").Change("1");
        cut.Find(".tree-list [data-tree='2haxe']").Click();
        // The save still marks Fatal Strike locked (the game recomputes locks on load), but its three requirements are learned.
        Assert.Contains("available", Skill(cut, "Fatal Strike").ClassList);
        Assert.False(Skill(cut, "Fatal Strike").HasAttribute("disabled"));

        Skill(cut, "Fatal Strike").Click();
        Assert.Equal(0, h.Editor.Current.Character[CharacterField.AbilityPoints]);
        Assert.Contains("learned", Skill(cut, "Fatal Strike").ClassList);

        Skill(cut, "Hooking Chop").Click();
        Assert.Contains("Unlearn Fatal Strike first", cut.Find(".banner-error").TextContent);
        Assert.Contains("learned", Skill(cut, "Hooking Chop").ClassList);
    }

    [Fact]
    public void Rules_toggle_is_disabled_until_rules_are_available()
    {
        var h = EditorServiceTests.Start(gameDir: Fixtures.GameDir);
        var session = h.Editor.Open("character_2", "exitsave_1");
        using var ctx = ShellTests.Context(h);

        var cut = ctx.Render<SkillsTab>(p => p.Add(x => x.Session, session with { RulesAvailable = false }));

        var toggle = cut.Find("input.rules");
        Assert.True(toggle.HasAttribute("disabled"));
        Assert.False(toggle.HasAttribute("checked"));
        Assert.Equal("Tree rules not loaded yet", cut.Find("label.rules-toggle").GetAttribute("title"));
    }
}

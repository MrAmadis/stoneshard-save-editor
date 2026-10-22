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

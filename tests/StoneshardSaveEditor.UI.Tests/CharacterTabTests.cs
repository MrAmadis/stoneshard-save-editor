using Bunit;
using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.UI.Screens;

namespace StoneshardSaveEditor.Tests;

public class CharacterTabTests
{
    private static (EditorServiceTests.Harness H, BunitContext Ctx, IRenderedComponent<Editor> Cut) OpenVelmir()
    {
        var h = EditorServiceTests.Start();
        h.Editor.Open("character_2", "exitsave_1");
        var ctx = ShellTests.Context(h);
        return (h, ctx, ctx.Render<Editor>());
    }

    [Fact]
    public void Character_tab_shows_strength_and_gold()
    {
        var (_, ctx, cut) = OpenVelmir();
        using var _ctx = ctx;

        Assert.Equal("13", cut.Find("[data-field='Strength'] input").GetAttribute("value"));
        Assert.Equal("2864", cut.Find("[data-field='Gold'] input").GetAttribute("value"));
        Assert.Contains("The game caps these at your maximum", cut.Markup);
        Assert.Contains("Changing these can make the game level you up when the save loads", cut.Markup);
    }

    [Fact]
    public void Plus_on_strength_makes_it_14()
    {
        var (h, ctx, cut) = OpenVelmir();
        using var _ctx = ctx;

        cut.Find("[data-field='Strength'] button.plus").Click();

        Assert.Equal(14, h.Editor.Current.Character[CharacterField.Strength]);
        cut.WaitForAssertion(() =>
            Assert.Equal("14", cut.Find("[data-field='Strength'] input").GetAttribute("value")));
    }

    [Fact]
    public void Rejected_edit_shows_its_message()
    {
        var (h, ctx, cut) = OpenVelmir();
        using var _ctx = ctx;

        cut.Find("[data-field='Strength'] input").Change("500");

        Assert.Contains("Strength must be a whole number between 1 and 100", cut.Find(".banner-error").TextContent);
        Assert.Equal(13, h.Editor.Current.Character[CharacterField.Strength]);
        Assert.Equal(0, h.Editor.Current.ChangeCount);
    }

    [Fact]
    public void Gold_edit_sets_gold()
    {
        var (h, ctx, cut) = OpenVelmir();
        using var _ctx = ctx;

        cut.Find("[data-field='Gold'] input").Change("3000");

        Assert.Equal(3000, h.Editor.Current.Gold);
    }

    [Fact]
    public void Reputation_lists_Osbrook_4258_first()
    {
        var (h, ctx, cut) = OpenVelmir();
        using var _ctx = ctx;

        cut.FindAll("button.tab").Single(b => b.TextContent == "Reputation").Click();

        var first = cut.FindAll(".rep")[0];
        Assert.Equal("Osbrook", first.QuerySelector(".nm")!.TextContent);
        Assert.Equal("4258", first.QuerySelector("input")!.GetAttribute("value"));

        first.QuerySelector("input")!.Change("4300");
        Assert.Equal(4300, h.Editor.Current.Reputation.Single(r => r.Name == "Osbrook").Value);
    }
}

using Bunit;
using StoneshardSaveEditor.GameData;
using StoneshardSaveEditor.UI.Screens;

namespace StoneshardSaveEditor.Tests;

public class InventoryTabTests
{
    private static (EditorServiceTests.Harness H, BunitContext Ctx, IRenderedComponent<Editor> Cut) OpenInventory(string? gameDir = null)
    {
        var h = EditorServiceTests.Start(gameDir: gameDir ?? Fixtures.GameDir);
        h.Editor.Open("character_2", "exitsave_1");
        var ctx = ShellTests.Context(h);
        var cut = ctx.Render<Editor>();
        cut.FindAll("button.tab").Single(b => b.TextContent == "Inventory").Click();
        return (h, ctx, cut);
    }

    [Fact]
    public void Moneybag_renders_at_its_cell()
    {
        var (_, ctx, cut) = OpenInventory();
        using var _ctx = ctx;

        var moneybag = cut.Find(".bag [data-item='o_inv_moneybag']");
        var style = moneybag.GetAttribute("style")!;
        Assert.Contains("left:162px", style);
        Assert.Contains("top:0px", style);
        Assert.Contains("height:108px", style);
        Assert.Equal("2864", moneybag.QuerySelector(".badge")!.TextContent);
        Assert.Contains("icon://s_inv_voulge", cut.Find(".doll [data-cell='weapon']").InnerHtml);
        Assert.Contains("icon://s_inv_guardcrossbow", cut.Find(".doll [data-cell='set2']").InnerHtml);
    }

    [Fact]
    public void Selecting_items_shows_their_fields()
    {
        var (h, ctx, cut) = OpenInventory();
        using var _ctx = ctx;

        cut.Find(".bag [data-item='o_inv_moneybag']").Click();
        Assert.Equal("2864", cut.Find(".item-detail [data-field='Gold'] input").GetAttribute("value"));

        var salve = h.Editor.Current.Items.First(i => i.Name == "o_inv_salve" && i.Charges == 3);
        cut.Find($".bag [data-index='{salve.Index}']").Click();
        Assert.Equal("3", cut.Find(".item-detail [data-field='Charges'] input").GetAttribute("value"));

        cut.Find(".doll [data-item='o_inv_leafshaped_bolts']").Click();
        cut.Find(".item-detail [data-field='Count'] input").Change("5");
        Assert.Equal(5, h.Editor.Current.Items.Single(i => i.Name == "o_inv_leafshaped_bolts").Count);
    }

    [Fact]
    public void Delete_removes_the_selected_item()
    {
        var (h, ctx, cut) = OpenInventory();
        using var _ctx = ctx;
        var count = h.Editor.Current.Items.Count;

        cut.Find(".bag [data-item='o_inv_cheese']").Click();
        cut.Find(".item-detail button.delete").Click();

        Assert.Equal(count - 1, h.Editor.Current.Items.Count);
        Assert.DoesNotContain(h.Editor.Current.Items, i => i.Name == "o_inv_cheese");
        Assert.Empty(cut.FindAll(".item-detail button.delete"));
    }

    [Fact]
    public void Catalog_search_and_chips_filter_and_unavailable_items_are_disabled()
    {
        var (h, ctx, cut) = OpenInventory();
        using var _ctx = ctx;
        var catalog = h.Editor.Catalog();

        cut.Find("button.add-item").Click();
        cut.Find(".catalog input.search").Input("treatise");

        var names = cut.FindAll(".catalog-item .nm").Select(e => e.TextContent).ToList();
        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.Contains("treatise", n, StringComparison.OrdinalIgnoreCase));

        cut.Find(".catalog input.search").Input("");
        Assert.Equal("Two-handed axe", cut.Find(".chips [data-category='2haxe']").TextContent);
        Assert.Equal("Ammo", cut.Find(".chips [data-category='ammo']").TextContent);
        cut.Find(".chips [data-category='ammo']").Click();
        var keys = cut.FindAll(".catalog-item").Select(b => b.GetAttribute("data-key")).ToList();
        Assert.NotEmpty(keys);
        Assert.All(keys, k => Assert.Equal("ammo", catalog.Single(i => i.Key == k).Category));

        cut.Find(".chips [data-category='']").Click();
        var unavailable = catalog.First(i => i.Availability == CatalogAvailability.Unavailable);
        cut.Find(".catalog input.search").Input(unavailable.DisplayName);
        var button = cut.Find($".catalog-item[data-key='{unavailable.Key}']");
        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal(unavailable.Reason, button.GetAttribute("title"));
    }

    [Fact]
    public void Picking_from_the_catalog_adds_the_item_and_closes()
    {
        var (h, ctx, cut) = OpenInventory();
        using var _ctx = ctx;
        var before = h.Editor.Current.Items.Count;

        cut.Find("button.add-item").Click();
        cut.Find(".catalog input.search").Input("gold");
        cut.Find(".catalog-item[data-key='o_inv_gold']").Click();

        Assert.Empty(cut.FindAll(".catalog"));
        var coins = h.Editor.Current.Items[before];
        Assert.Equal(("o_inv_gold", 100.0), (coins.Name, coins.Count));
    }

    [Fact]
    public void No_room_in_the_bag_is_shown()
    {
        var (h, ctx, cut) = OpenInventory();
        using var _ctx = ctx;
        var torch = h.Editor.Current.Items.First(i => i.Name == "o_inv_torch");

        cut.Find($".bag [data-index='{torch.Index}']").Click();
        for (var i = 0; i < 50 && cut.FindAll(".banner-error").Count == 0; i++)
            cut.Find(".item-detail button.duplicate").Click();

        Assert.Contains("No room in the bag", cut.Find(".banner-error").TextContent);
    }

    [Fact]
    public void Add_item_needs_game_files()
    {
        var junk = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(junk, "data.win"), "junk");
        File.WriteAllText(Path.Combine(junk, "StoneShard.exe"), "junk");
        var (_, ctx, cut) = OpenInventory(gameDir: junk);
        using var _ctx = ctx;

        var add = cut.Find("button.add-item");

        Assert.True(add.HasAttribute("disabled"));
        Assert.Equal("Game files not found", add.GetAttribute("title"));
    }
}

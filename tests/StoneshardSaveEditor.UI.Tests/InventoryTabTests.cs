using Bunit;
using StoneshardSaveEditor.Core.Editing;
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
        Assert.Null(moneybag.QuerySelector(".badge")); // no coins inside: its cached 2864 is stale
        Assert.Contains("icon://s_inv_voulge", cut.Find(".doll [data-cell='weapon-right']").InnerHtml);
        Assert.Contains("icon://s_inv_guardcrossbow", cut.Find(".doll [data-cell='set2-right']").InnerHtml);
        Assert.NotNull(cut.Find(".doll [data-cell='set2-left'] [data-item='o_inv_leafshaped_bolts']"));
    }

    [Fact]
    public void Selecting_items_shows_their_fields()
    {
        var (h, ctx, cut) = OpenInventory();
        using var _ctx = ctx;

        cut.Find(".bag [data-item='o_inv_moneybag']").Click();
        Assert.Equal("0", cut.Find(".item-detail [data-field='Gold'] input").GetAttribute("value"));
        cut.Find(".item-detail [data-field='Gold'] input").Change("1500");
        Assert.Equal(1500, h.Editor.Current.Gold);
        Assert.Equal("1500", cut.Find(".bag [data-item='o_inv_moneybag'] .badge").TextContent);
        cut.Find(".item-detail [data-field='Gold'] input").Change("2500");
        Assert.Contains("A purse holds at most 2000 gold", cut.Find(".banner-error").TextContent);

        var salve = h.Editor.Current.Items.First(i => i.Name == "o_inv_salve" && i.Charges == 3);
        cut.Find($".bag [data-index='{salve.Index}']").Click();
        Assert.Equal("3", cut.Find(".item-detail [data-field='Charges'] input").GetAttribute("value"));
        cut.Find(".item-detail [data-field='Charges'] input").Change("6");
        Assert.Contains("between 1 and 3", cut.Find(".item-detail [data-field='Charges'] .field-error").TextContent);
        Assert.Equal(3, h.Editor.Current.Items[salve.Index].Charges);

        cut.Find(".doll [data-item='o_inv_leafshaped_bolts']").Click();
        cut.Find(".item-detail [data-field='Count'] input").Change("5");
        Assert.Equal(5, h.Editor.Current.Items.Single(i => i.Name == "o_inv_leafshaped_bolts").Count);
    }

    [Fact]
    public void Selection_stays_on_the_item_when_an_earlier_one_is_deleted()
    {
        var (h, ctx, cut) = OpenInventory();
        using var _ctx = ctx;
        var salve = h.Editor.Current.Items.First(i => i.Name == "o_inv_salve" && i.Charges == 2);
        cut.Find($".bag [data-index='{salve.Index}']").Click();

        h.Editor.DeleteItem(h.Editor.Current.Items.First(i => i.Name == "o_inv_cheese").Index);

        cut.WaitForAssertion(() => Assert.Equal((salve.Index - 1).ToString(), cut.Find(".bag .it.sel").GetAttribute("data-index")));
        Assert.Equal("2", cut.Find(".item-detail [data-field='Charges'] input").GetAttribute("value"));
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

    private static AngleSharp.Dom.IElement Target(IRenderedComponent<Editor> cut, string slot, int set = 0) =>
        cut.Find($".gear-slot[data-slot='{slot}'][data-set='{set}']");

    private static AngleSharp.Dom.IElement AddToBag(EditorServiceTests.Harness h, IRenderedComponent<Editor> cut, string key)
    {
        h.Editor.AddItem(key);
        return cut.WaitForElement($".bag [data-item='{key}']");
    }

    private static string Places(EditorServiceTests.Harness h) =>
        string.Join(";", h.Editor.Current.Items.Select(i => $"{i.Name}@{i.Place}"));

    [Fact]
    public void Dragging_lights_the_slots_an_item_fits()
    {
        var (h, ctx, cut) = OpenInventory();
        using var _ctx = ctx;
        var hood = AddToBag(h, cut, "Hood");

        hood.DragStart();
        Assert.Contains("ok", Target(cut, GearRules.Head).ClassList);
        Assert.Contains("no", Target(cut, GearRules.Feet).ClassList);

        cut.Find(".bag [data-item='Hood']").DragEnd();
        Assert.Empty(cut.FindAll(".gear-slot.ok, .gear-slot.no"));
    }

    [Fact]
    public void Dropping_a_helmet_on_the_head_swaps_it()
    {
        var (h, ctx, cut) = OpenInventory();
        using var _ctx = ctx;
        AddToBag(h, cut, "Hood").DragStart();

        Target(cut, GearRules.Head).Drop();

        Assert.Equal(GearRules.Head, h.Editor.Current.Items.Single(i => i.Name == "Hood").Slot);
        Assert.False(h.Editor.Current.Items.Single(i => i.Name == "Flat Kettle Helmet").Equipped);
        Assert.Equal("Hood", cut.Find(".item-detail .head b").TextContent);
    }

    [Fact]
    public void Dropping_where_the_game_wouldnt_allow_does_nothing()
    {
        var (h, ctx, cut) = OpenInventory();
        using var _ctx = ctx;
        AddToBag(h, cut, "Hood").DragStart();
        var before = Places(h);

        Target(cut, GearRules.Feet).Drop();

        Assert.Equal(before, Places(h));
        Assert.Empty(cut.FindAll(".banner-error"));
    }

    [Fact]
    public void Dropping_on_its_own_slot_does_nothing()
    {
        var (h, ctx, cut) = OpenInventory();
        using var _ctx = ctx;

        cut.Find(".doll [data-cell='head'] .equipped").DragStart();
        Target(cut, GearRules.Head).Drop();

        Assert.Equal(0, h.Editor.Current.ChangeCount);
    }

    [Fact]
    public void A_swapped_out_item_with_no_room_is_reported()
    {
        var (h, ctx, cut) = OpenInventory();
        using var _ctx = ctx;
        var voulge = h.Editor.Current.Items.Single(i => i.Name == "Voulge");
        var bow = h.Editor.Catalog().First(c => c.Category == "bow" && c.Availability == CatalogAvailability.Ready && c.W * c.H < voulge.W * voulge.H);
        AddToBag(h, cut, bow.Key);
        // Fill every free cell, so the only room left is the bow's spot, smaller than the Voulge.
        var small = h.Editor.Current.Items.First(i => !i.Equipped && i.W == 1 && i.H == 1).Index;
        for (var i = 0; i < 50; i++)
        {
            try { h.Editor.DuplicateItem(small); }
            catch (EditRejectedException) { break; }
        }

        cut.WaitForElement($".bag [data-item='{bow.Key}']").DragStart();
        Target(cut, GearRules.RightHand).Drop();

        Assert.Contains(cut.FindAll(".banner-warn"), b => b.TextContent.Contains("Voulge had no room and was removed"));
    }
}

using StoneshardSaveEditor.Core.Editing;

namespace StoneshardSaveEditor.Tests.Editing;

public class GearRulesTests
{
    [Theory]
    [InlineData("Head", "o_inv_head")]
    [InlineData("Chest", "o_inv_armor")]
    [InlineData("Arms", "o_inv_gloves")]
    [InlineData("Legs", "o_inv_boots")]
    [InlineData("Waist", "o_inv_belt")]
    [InlineData("Amulet", "o_inv_neck")]
    [InlineData("Back", "o_inv_back")]
    [InlineData("Ring", "o_inv_ring_1 o_inv_ring_2")]
    [InlineData("shield", "o_inv_left_hand")]
    [InlineData("sword", "o_inv_right_hand o_inv_left_hand")]
    [InlineData("axe", "o_inv_right_hand o_inv_left_hand")]
    [InlineData("mace", "o_inv_right_hand o_inv_left_hand")]
    [InlineData("dagger", "o_inv_right_hand o_inv_left_hand")]
    [InlineData("2hsword", "o_inv_right_hand")]
    [InlineData("2haxe", "o_inv_right_hand")]
    [InlineData("2hmace", "o_inv_right_hand")]
    [InlineData("2hStaff", "o_inv_right_hand")]
    [InlineData("spear", "o_inv_right_hand")]
    [InlineData("bow", "o_inv_right_hand")]
    [InlineData("crossbow", "o_inv_right_hand")]
    [InlineData("ammo", "o_inv_left_hand")]
    [InlineData("wand", "")] // open until the in-game check (spec §5.1)
    [InlineData("tool", "")]
    [InlineData("food", "")]
    [InlineData("", "")]
    public void Slots_follow_the_game_tables(string kind, string slots) =>
        Assert.Equal(slots.Split(' ', StringSplitOptions.RemoveEmptyEntries), GearRules.SlotsFor(kind));

    [Theory]
    [InlineData("2hsword", true)]
    [InlineData("2haxe", true)]
    [InlineData("2hmace", true)]
    [InlineData("2hStaff", true)]
    [InlineData("spear", true)]
    [InlineData("bow", true)]
    [InlineData("crossbow", true)]
    [InlineData("sword", false)]
    [InlineData("shield", false)]
    [InlineData("ammo", false)]
    public void Two_handed_kinds(string kind, bool twoHanded) => Assert.Equal(twoHanded, GearRules.IsTwoHanded(kind));

    [Theory]
    [InlineData("crossbow", "o_inv_leafshaped_bolts", true)]
    [InlineData("bow", "o_inv_bodkin_arrows", true)]
    [InlineData("bow", "o_inv_leafshaped_bolts", false)]
    [InlineData("spear", "o_inv_leafshaped_bolts", false)]
    public void Bows_shoot_arrows_and_crossbows_bolts(string weapon, string ammo, bool shoots) =>
        Assert.Equal(shoots, GearRules.Shoots(weapon, ammo));

    [Fact]
    public void Hands_refuse_what_the_game_wouldnt_hold()
    {
        Assert.Null(GearRules.HandRefusal("2hsword", "x", GearRules.RightHand, null));
        Assert.Equal("A two-handed weapon needs the other hand free", GearRules.HandRefusal("2hsword", "x", GearRules.RightHand, ("shield", "s")));
        Assert.Null(GearRules.HandRefusal("crossbow", "x", GearRules.RightHand, ("ammo", "o_inv_leafshaped_bolts")));
        Assert.Equal("The other hand holds a two-handed weapon", GearRules.HandRefusal("shield", "s", GearRules.LeftHand, ("spear", "Voulge")));
        Assert.Null(GearRules.HandRefusal("ammo", "o_inv_leafshaped_bolts", GearRules.LeftHand, ("crossbow", "Guard Crossbow")));
        Assert.Equal("Ammunition goes with a bow or crossbow in the other hand", GearRules.HandRefusal("ammo", "o_inv_arrows", GearRules.LeftHand, null));
        Assert.Null(GearRules.HandRefusal("shield", "s", GearRules.LeftHand, ("sword", "w")));
    }
}

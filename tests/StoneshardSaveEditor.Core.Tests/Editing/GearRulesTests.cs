using StoneshardSaveEditor.Core.Editing;

namespace StoneshardSaveEditor.Tests.Editing;

public class GearRulesTests
{
    [Theory]
    [InlineData("Head", "x", "o_inv_head")]
    [InlineData("Chest", "x", "o_inv_armor")]
    [InlineData("Arms", "x", "o_inv_gloves")]
    [InlineData("Legs", "x", "o_inv_boots")]
    [InlineData("Waist", "x", "o_inv_belt")]
    [InlineData("Amulet", "x", "o_inv_neck")]
    [InlineData("Back", "x", "o_inv_back")]
    [InlineData("Ring", "x", "o_inv_ring_1 o_inv_ring_2")]
    [InlineData("shield", "x", "o_inv_left_hand")]
    [InlineData("sword", "x", "o_inv_right_hand o_inv_left_hand")]
    [InlineData("axe", "x", "o_inv_right_hand o_inv_left_hand")]
    [InlineData("mace", "x", "o_inv_right_hand o_inv_left_hand")]
    [InlineData("dagger", "x", "o_inv_right_hand o_inv_left_hand")]
    [InlineData("2hsword", "x", "o_inv_right_hand")]
    [InlineData("2haxe", "x", "o_inv_right_hand")]
    [InlineData("2hmace", "x", "o_inv_right_hand")]
    [InlineData("2hStaff", "x", "o_inv_right_hand")]
    [InlineData("spear", "x", "o_inv_right_hand")]
    [InlineData("bow", "x", "o_inv_right_hand")]
    [InlineData("crossbow", "x", "o_inv_right_hand")]
    [InlineData("ammo", "x", "o_inv_left_hand")]
    [InlineData("wand", "x", "")] // unknown: the editor can't make one to check in-game
    [InlineData("food", "x", "")]
    [InlineData("", "x", "")]
    // Session G2: slings either hand; lutes, chains and pickaxes take both hands; the sickle and the torch either hand.
    [InlineData("sling", "x", "o_inv_right_hand o_inv_left_hand")]
    [InlineData("lute", "x", "o_inv_right_hand")]
    [InlineData("chain", "x", "o_inv_right_hand")]
    [InlineData("pick", "x", "o_inv_right_hand")]
    [InlineData("tool", "Sickle", "o_inv_right_hand o_inv_left_hand")]
    [InlineData("tool", "o_inv_torch", "o_inv_right_hand o_inv_left_hand")]
    [InlineData("tool", "o_inv_bomb_fire", "")] // other tools from the items table (bombs, nets, bedrolls) aren't worn
    public void Slots_follow_the_game_tables(string kind, string name, string slots) =>
        Assert.Equal(slots.Split(' ', StringSplitOptions.RemoveEmptyEntries), GearRules.SlotsFor(kind, name));

    [Theory]
    [InlineData("2hsword", true)]
    [InlineData("2haxe", true)]
    [InlineData("2hmace", true)]
    [InlineData("2hStaff", true)]
    [InlineData("spear", true)]
    [InlineData("bow", true)]
    [InlineData("crossbow", true)]
    [InlineData("lute", true)]
    [InlineData("chain", true)]
    [InlineData("pick", true)]
    [InlineData("sling", false)]
    [InlineData("sword", false)]
    [InlineData("shield", false)]
    [InlineData("ammo", false)]
    public void Two_handed_kinds(string kind, bool twoHanded) => Assert.Equal(twoHanded, GearRules.IsTwoHanded(kind));

    [Theory]
    [InlineData("crossbow", "o_inv_leafshaped_bolts", true)]
    [InlineData("bow", "o_inv_bodkin_arrows", true)]
    [InlineData("bow", "o_inv_leafshaped_bolts", false)]
    [InlineData("spear", "o_inv_leafshaped_bolts", false)]
    [InlineData("sling", "o_inv_ammo_stone", true)] // session G2: stones in the other hand
    [InlineData("sling", "o_inv_ammo_bullet", true)]
    [InlineData("sling", "o_inv_arrows", false)]
    public void Bows_shoot_arrows_crossbows_bolts_and_slings_stones(string weapon, string ammo, bool shoots) =>
        Assert.Equal(shoots, GearRules.Shoots(weapon, ammo));

    [Fact]
    public void Hands_refuse_what_the_game_wouldnt_hold()
    {
        Assert.Null(GearRules.HandRefusal("2hsword", "x", GearRules.RightHand, null));
        Assert.Equal("A two-handed weapon needs the other hand free", GearRules.HandRefusal("2hsword", "x", GearRules.RightHand, ("shield", "s")));
        Assert.Null(GearRules.HandRefusal("crossbow", "x", GearRules.RightHand, ("ammo", "o_inv_leafshaped_bolts")));
        Assert.Equal("The other hand holds a two-handed weapon", GearRules.HandRefusal("shield", "s", GearRules.LeftHand, ("spear", "Voulge")));
        Assert.Null(GearRules.HandRefusal("ammo", "o_inv_leafshaped_bolts", GearRules.LeftHand, ("crossbow", "Guard Crossbow")));
        Assert.Equal("Ammunition goes with a bow, crossbow or sling in the other hand", GearRules.HandRefusal("ammo", "o_inv_arrows", GearRules.LeftHand, null));
        Assert.Null(GearRules.HandRefusal("shield", "s", GearRules.LeftHand, ("sword", "w")));
    }
}

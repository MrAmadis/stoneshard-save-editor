using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Tests.Editing;

public class SkillsEditorTests
{
    private const string WideCut = "o_skill_wide_cut_ico";

    private static (SaveSession Session, SkillsEditor Skills) Open()
    {
        var session = Sessions.OpenVelmir();
        return (session, new SkillsEditor(session));
    }

    private static GmNode Flag(SaveSession s, string obj, int field)
    {
        var list = (GmArray)((GmObject)s.Root["skillsDataMap"]!)["skillsAllDataList"]!;
        var i = list.Items.FindIndex(n => n is GmString str && str.Value == obj);
        return list.Items[i + field];
    }

    private static string Sp(SaveSession s) => ((GmNumber)((GmObject)s.Root["characterDataMap"]!)["SP"]!).Raw;

    private static List<GmNode> HotbarRow(SaveSession s, int row) =>
        ((GmArray)((GmArray)((GmObject)s.Root["skillsDataMap"]!)["skillsPanelDataList"]!).Items[row]).Items;

    [Fact]
    public void Read_lists_232_skills_11_learned()
    {
        var (_, skills) = Open();
        var all = skills.Read();
        Assert.Equal(232, all.Count);
        Assert.Equal(11, all.Count(s => s.Learned));
        Assert.True(all.Single(s => s.Object == "o_pass_skill_fatal_hit").Locked);
        Assert.False(all.Single(s => s.Object == WideCut).Locked);
        Assert.Equal(0, skills.AbilityPoints);
    }

    [Fact]
    public void Innate_skills_cannot_be_unlearned()
    {
        var (session, skills) = Open();
        Assert.Throws<EditRejectedException>(() => skills.Unlearn("o_skill_craft_ico", refundPoint: true));
        Assert.Equal(0, session.ChangeCount);
    }

    [Fact]
    public void Unlearn_refunds_and_clears_hotbar()
    {
        var (session, skills) = Open();
        Assert.Equal("o_skill_wide_cut", ((GmString)HotbarRow(session, 0)[0]).Value);

        skills.Unlearn(WideCut, refundPoint: true);

        Assert.Equal("0.0", ((GmNumber)Flag(session, WideCut, 1)).Raw);
        Assert.Equal("1.0", Sp(session));
        Assert.Equal("-4.0", ((GmNumber)HotbarRow(session, 0)[0]).Raw);
        Assert.Equal("o_skill_dismember", ((GmString)HotbarRow(session, 0)[1]).Value);
        Assert.Equal(1, session.ChangeCount);
    }

    [Fact]
    public void Unlearn_without_refund_keeps_points()
    {
        var (session, skills) = Open();
        skills.Unlearn(WideCut, refundPoint: false);
        Assert.Equal("0.0", Sp(session));
    }

    [Fact]
    public void RespecAll_refunds_six()
    {
        var (session, skills) = Open();

        Assert.Equal(6, skills.RespecAll());

        Assert.Equal(6, skills.AbilityPoints);
        Assert.Equal(SkillsEditor.Innate.Order(), skills.Read().Where(s => s.Learned).Select(s => s.Object).Order());
        Assert.Equal(1, session.ChangeCount);
    }

    [Fact]
    public void Learn_spends_point()
    {
        var (session, skills) = Open();
        skills.RespecAll();

        skills.Learn(WideCut, spendPoint: true);

        var state = skills.Read().Single(s => s.Object == WideCut);
        Assert.True(state.Learned);
        Assert.False(state.Locked);
        Assert.Equal("5.0", Sp(session));
        Assert.Equal("1.0", ((GmNumber)Flag(session, WideCut, 1)).Raw);
    }

    [Fact]
    public void Learn_without_points_is_rejected()
    {
        var (session, skills) = Open();
        Assert.Throws<EditRejectedException>(() => skills.Learn("o_pass_skill_fatal_hit", spendPoint: true));
        Assert.False(skills.Read().Single(s => s.Object == "o_pass_skill_fatal_hit").Learned);
        Assert.Equal(0, session.ChangeCount);
    }

    [Fact]
    public void Learn_for_free_and_twice_is_rejected()
    {
        var (_, skills) = Open();
        skills.Learn("o_pass_skill_fatal_hit", spendPoint: false);
        Assert.Equal(0, skills.AbilityPoints);
        Assert.Throws<EditRejectedException>(() => skills.Learn("o_pass_skill_fatal_hit", spendPoint: false));
        Assert.Throws<EditRejectedException>(() => skills.Learn("o_skill_not_real_ico", spendPoint: false));
    }

    [Fact]
    public void Unlock_clears_locked_flag()
    {
        var (session, skills) = Open();
        skills.Unlock(["o_skill_flame_ring_ico"]);
        Assert.False(skills.Read().Single(s => s.Object == "o_skill_flame_ring_ico").Locked);
        Assert.Equal("0.0", ((GmNumber)Flag(session, "o_skill_flame_ring_ico", 3)).Raw);

        skills.Lock(["o_skill_flame_ring_ico"]);
        Assert.Equal("1.0", ((GmNumber)Flag(session, "o_skill_flame_ring_ico", 3)).Raw);
    }

    [Fact]
    public void UnlockTree_unlocks_first_tier_and_records_the_book_like_reading_it()
    {
        var (session, skills) = Open();

        skills.UnlockTree(["o_skill_flame_ring_ico", "o_pass_skill_pyromania"], "treatise_pyro1");

        Assert.False(skills.Read().Single(s => s.Object == "o_skill_flame_ring_ico").Locked);
        Assert.False(skills.Read().Single(s => s.Object == "o_pass_skill_pyromania").Locked);
        var books = ((GmArray)((GmObject)session.Root["characterDataMap"]!)["Books_Read"]!).Items.OfType<GmString>().Select(s => s.Value).ToList();
        Assert.Equal("treatise_pyro1", books[^1]);
        Assert.Equal(1, session.ChangeCount);

        skills.UnlockTree(["o_skill_flame_ring_ico"], "treatise_pyro1");
        Assert.Single(((GmArray)((GmObject)session.Root["characterDataMap"]!)["Books_Read"]!).Items.OfType<GmString>(), s => s.Value == "treatise_pyro1");
    }

    [Fact]
    public void RefundTree_unlearns_only_learned_listed()
    {
        var (_, skills) = Open();
        Assert.Equal(3, skills.RefundTree([WideCut, "o_skill_dismember_ico", "o_pass_skill_shieldbreaker", "o_skill_killing_swing_ico"]));
        Assert.Equal(3, skills.AbilityPoints);
        Assert.True(skills.Read().Single(s => s.Object == "o_skill_seize_the_initiative_ico").Learned);
    }
}

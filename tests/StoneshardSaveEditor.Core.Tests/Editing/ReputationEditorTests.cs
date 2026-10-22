using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Tests.Editing;

public class ReputationEditorTests
{
    private static GmObject Location(GmObject root, string key) =>
        (GmObject)((GmObject)((GmObject)root["globalmapDataMap"]!)["locationsDataMap"]!)[key]!;

    [Fact]
    public void Reputation_reads_five_settlements()
    {
        var settlements = new ReputationEditor(Sessions.OpenVelmir()).Read();

        Assert.Equal(
            new[]
            {
                new SettlementReputation("Brynn", "26_33", 2648),
                new SettlementReputation("Denbrie", "14_37", 2552),
                new SettlementReputation("Mannshire", "26_23", 2552),
                new SettlementReputation("Osbrook", "32_10", 4258),
                new SettlementReputation("RottenWillow", "13_9", 2500),
            },
            settlements.OrderBy(s => s.VillageKey));
    }

    [Fact]
    public void Reputation_set_updates_only_value()
    {
        var session = Sessions.OpenVelmir();
        new ReputationEditor(session).Set("32_10", 9000);

        var osbrook = Location(session.Root, "32_10");
        Assert.Equal("9000.0", ((GmNumber)osbrook["reputation"]!).Raw);
        Assert.Equal(7, ((GmArray)osbrook["reputationLogList"]!).Items.Count);
        Assert.Equal(1, session.ChangeCount);
    }

    [Theory]
    [InlineData("99_99", 10)]
    [InlineData("32_10", 100001)]
    [InlineData("32_10", -1)]
    [InlineData("32_10", 10.5)]
    public void Reputation_rejects_unknown_location_and_out_of_range(string location, double value)
    {
        var session = Sessions.OpenVelmir();
        Assert.Throws<EditRejectedException>(() => new ReputationEditor(session).Set(location, value));
        Assert.Equal("4258.0", ((GmNumber)Location(session.Root, "32_10")["reputation"]!).Raw);
        Assert.Equal(0, session.ChangeCount);
    }
}

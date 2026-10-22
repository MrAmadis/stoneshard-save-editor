using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Tests.Editing;

public class CharacterEditorTests
{
    [Fact]
    public void Read_returns_velmir_values()
    {
        var values = new CharacterEditor(Sessions.OpenVelmir()).Read();

        Assert.Equal(13, values[CharacterField.Strength]);
        Assert.Equal(11, values[CharacterField.Agility]);
        Assert.Equal(11, values[CharacterField.Perception]);
        Assert.Equal(12, values[CharacterField.Vitality]);
        Assert.Equal(10, values[CharacterField.Willpower]);
        Assert.Equal(5, values[CharacterField.Level]);
        Assert.Equal(1237, values[CharacterField.Experience]);
        Assert.Equal(0, values[CharacterField.StatPoints]);
        Assert.Equal(0, values[CharacterField.AbilityPoints]);
        Assert.Equal(100, values[CharacterField.Health]);
        Assert.Equal(104, values[CharacterField.Energy]);
        Assert.Equal(51, values[CharacterField.Sanity]);
        Assert.Equal(78, values[CharacterField.Morale]);
        Assert.Equal(9.04, values[CharacterField.Thirst], 2);
        Assert.Equal(18, values.Count);
    }

    [Fact]
    public void Set_writes_gamemaker_real()
    {
        var session = Sessions.OpenVelmir();
        new CharacterEditor(session).Set(CharacterField.Strength, 18);

        Assert.Equal("18.0", ((GmNumber)((GmObject)session.Root["characterDataMap"]!)["STR"]!).Raw);
        Assert.Equal(1, session.ChangeCount);
    }

    [Fact]
    public void Set_maps_fields_to_game_keys()
    {
        var session = Sessions.OpenVelmir();
        var editor = new CharacterEditor(session);
        editor.Set(CharacterField.AbilityPoints, 4);
        editor.Set(CharacterField.StatPoints, 3);
        editor.Set(CharacterField.Thirst, 0);

        var character = (GmObject)session.Root["characterDataMap"]!;
        Assert.Equal("4.0", ((GmNumber)character["SP"]!).Raw);
        Assert.Equal("3.0", ((GmNumber)character["AP"]!).Raw);
        Assert.Equal("0.0", ((GmNumber)character["Thirsty"]!).Raw);
    }

    [Theory]
    [InlineData(CharacterField.Strength, 12.5)]
    [InlineData(CharacterField.Strength, -1)]
    [InlineData(CharacterField.Strength, 101)]
    [InlineData(CharacterField.Sanity, 101)]
    [InlineData(CharacterField.Level, 0)]
    [InlineData(CharacterField.Experience, double.NaN)]
    [InlineData(CharacterField.AbilityPoints, double.PositiveInfinity)]
    [InlineData(CharacterField.Health, 10000)]
    public void Set_rejects_invalid(CharacterField field, double value)
    {
        var session = Sessions.OpenVelmir();
        var editor = new CharacterEditor(session);
        var before = editor.Read()[field];

        Assert.Throws<EditRejectedException>(() => editor.Set(field, value));

        Assert.Equal(before, editor.Read()[field]);
        Assert.Equal(0, session.ChangeCount);
    }

    [Fact]
    public void Rejection_message_names_field_and_range()
    {
        var editor = new CharacterEditor(Sessions.OpenVelmir());
        var e = Assert.Throws<EditRejectedException>(() => editor.Set(CharacterField.Sanity, 150));
        Assert.Equal("Sanity must be between 0 and 100", e.Message);
    }

    [Fact]
    public void Health_accepts_fractions()
    {
        var session = Sessions.OpenVelmir();
        new CharacterEditor(session).Set(CharacterField.Health, 55.5);
        Assert.Equal("55.5", ((GmNumber)((GmObject)session.Root["characterDataMap"]!)["HP"]!).Raw);
    }
}

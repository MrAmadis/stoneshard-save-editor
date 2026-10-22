using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Core.Editing;

public enum CharacterField
{
    Strength, Agility, Perception, Vitality, Willpower,
    Level, Experience, StatPoints, AbilityPoints,
    Health, Energy, Sanity, Morale, Hunger, Thirst, Fatigue, Intoxication, Pain,
}

/// <summary>Attributes, progress and condition values in <c>characterDataMap</c>.</summary>
public sealed class CharacterEditor(SaveSession session)
{
    private sealed record Rule(string Key, string Label, double Min, double Max, bool Whole);

    // Limits are a judgement call (design §10.6); revisit after the in-game checks.
    private static readonly Dictionary<CharacterField, Rule> Rules = new()
    {
        [CharacterField.Strength] = new("STR", "Strength", 1, 100, true),
        [CharacterField.Agility] = new("AGL", "Agility", 1, 100, true),
        [CharacterField.Perception] = new("PRC", "Perception", 1, 100, true),
        [CharacterField.Vitality] = new("Vitality", "Vitality", 1, 100, true),
        [CharacterField.Willpower] = new("WIL", "Willpower", 1, 100, true),
        [CharacterField.Level] = new("LVL", "Level", 1, 99, true),
        [CharacterField.Experience] = new("XP", "Experience", 0, 10_000_000, true),
        [CharacterField.StatPoints] = new("AP", "Stat points", 0, 999, true),
        [CharacterField.AbilityPoints] = new("SP", "Ability points", 0, 999, true),
        [CharacterField.Health] = new("HP", "Health", 0, 9_999, false),
        [CharacterField.Energy] = new("MP", "Energy", 0, 9_999, false),
        [CharacterField.Sanity] = new("Sanity", "Sanity", 0, 100, false),
        [CharacterField.Morale] = new("Morale", "Morale", 0, 100, false),
        [CharacterField.Hunger] = new("Hunger", "Hunger", 0, 100, false),
        [CharacterField.Thirst] = new("Thirsty", "Thirst", 0, 100, false),
        [CharacterField.Fatigue] = new("Fatigue", "Fatigue", 0, 100, false),
        [CharacterField.Intoxication] = new("Intoxication", "Intoxication", 0, 100, false),
        [CharacterField.Pain] = new("Pain", "Pain", 0, 100, false),
    };

    private GmObject Character => session.Root["characterDataMap"] as GmObject
                                  ?? throw new EditRejectedException("This save has no character data");

    public IReadOnlyDictionary<CharacterField, double> Read()
    {
        var character = Character;
        return Rules
            .Where(r => character[r.Value.Key] is GmNumber or GmBool)
            .ToDictionary(r => r.Key, r => character[r.Value.Key]!.AsDouble());
    }

    public void Set(CharacterField field, double value)
    {
        var rule = Rules[field];
        Limits.Check(rule.Label, value, rule.Min, rule.Max, rule.Whole);
        var character = Character;
        if (character[rule.Key] is null)
            throw new EditRejectedException($"This save has no {rule.Label} value");
        character.Set(rule.Key, GmNumber.From(value));
        session.MarkChanged();
    }
}

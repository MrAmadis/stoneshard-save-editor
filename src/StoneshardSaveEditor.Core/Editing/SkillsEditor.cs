using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;

namespace StoneshardSaveEditor.Core.Editing;

public sealed record SkillState(string Object, bool Learned, bool Locked);

/// <summary>
/// Skills in <c>skillsDataMap.skillsAllDataList</c>: a flat list of 5-value entries <c>[object, learned, ?, locked, ?]</c>.
/// Ability points are <c>characterDataMap.SP</c>; the hotbar is <c>skillsPanelDataList</c> (5 rows × 10).
/// </summary>
public sealed class SkillsEditor(SaveSession session)
{
    private const int Stride = 5, LearnedField = 1, LockedField = 3;

    /// <summary>Skills every new character has; they were never bought, so they are never refunded.</summary>
    public static IReadOnlySet<string> Innate { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "o_skill_butchering_ico", "o_skill_torch_strike_ico", "o_skill_craft_ico", "o_pass_skill_Sudden_Attacks", "o_skill_trap_search_ico",
    };

    private GmObject SkillsMap => session.Root["skillsDataMap"] as GmObject
                                  ?? throw new EditRejectedException("This save has no skills");

    private GmArray List => SkillsMap["skillsAllDataList"] as GmArray
                            ?? throw new EditRejectedException("This save has no skill list");

    private GmObject Character => session.Root["characterDataMap"] as GmObject
                                  ?? throw new EditRejectedException("This save has no character data");

    public int AbilityPoints => Character["SP"] is { } sp ? (int)sp.AsDouble() : 0;

    public IReadOnlyList<SkillState> Read() => ReadFrom(session.Root);

    /// <summary>Reads skill flags from any parsed save, without opening a session.</summary>
    public static IReadOnlyList<SkillState> ReadFrom(GmObject saveRoot)
    {
        if ((saveRoot["skillsDataMap"] as GmObject)?["skillsAllDataList"] is not GmArray list)
            return [];
        var items = list.Items;
        var result = new List<SkillState>(items.Count / Stride);
        for (var i = 0; i + Stride <= items.Count; i += Stride)
            if (items[i] is GmString name)
                result.Add(new SkillState(name.Value, items[i + LearnedField].IsTruthy(), items[i + LockedField].IsTruthy()));
        return result;
    }

    public void Learn(string obj, bool spendPoint)
    {
        var i = IndexOf(obj);
        if (List.Items[i + LearnedField].IsTruthy())
            throw new EditRejectedException("That skill is already learned");
        if (spendPoint && AbilityPoints < 1)
            throw new EditRejectedException("No ability points left to spend");

        SetLearned(i, true);
        List.Items[i + LockedField] = GmNumber.From(0);
        if (spendPoint)
            AddPoints(-1);
        session.MarkChanged();
    }

    public void Unlearn(string obj, bool refundPoint)
    {
        if (Innate.Contains(obj))
            throw new EditRejectedException("Starting skills can't be unlearned");
        var i = IndexOf(obj);
        if (!List.Items[i + LearnedField].IsTruthy())
            throw new EditRejectedException("That skill isn't learned");
        UnlearnAt(i, obj, refundPoint);
        session.MarkChanged();
    }

    /// <summary>Unlearns every skill that was bought and refunds a point for each.</summary>
    public int RespecAll() => RefundTree(Read().Select(s => s.Object));

    public int RefundTree(IEnumerable<string> objs)
    {
        var refunded = 0;
        foreach (var obj in objs.Distinct())
        {
            if (Innate.Contains(obj))
                continue;
            var i = IndexOfOrNull(obj);
            if (i is null || !List.Items[i.Value + LearnedField].IsTruthy())
                continue;
            UnlearnAt(i.Value, obj, refund: true);
            refunded++;
        }
        if (refunded > 0)
            session.MarkChanged();
        return refunded;
    }

    public void Unlock(IEnumerable<string> objs) => SetLocked(objs, locked: false);

    public void Lock(IEnumerable<string> objs) => SetLocked(objs, locked: true);

    private void SetLocked(IEnumerable<string> objs, bool locked)
    {
        var changed = false;
        foreach (var obj in objs.Distinct())
        {
            var i = IndexOf(obj);
            if (List.Items[i + LockedField].IsTruthy() == locked)
                continue;
            List.Items[i + LockedField] = GmNumber.From(locked ? 1 : 0);
            changed = true;
        }
        if (changed)
            session.MarkChanged();
    }

    private void UnlearnAt(int i, string obj, bool refund)
    {
        SetLearned(i, false);
        if (refund)
            AddPoints(1);
        RemoveFromHotbar(obj);
    }

    private void SetLearned(int i, bool learned) => List.Items[i + LearnedField] = GmNumber.From(learned ? 1 : 0);

    private void AddPoints(int delta) => Character.Set("SP", GmNumber.From(AbilityPoints + delta));

    /// <summary>The hotbar names actives without the <c>_ico</c> suffix; empty slots are -4.</summary>
    private void RemoveFromHotbar(string obj)
    {
        var hotbarName = obj.EndsWith("_ico", StringComparison.Ordinal) ? obj[..^"_ico".Length] : obj;
        if (SkillsMap["skillsPanelDataList"] is not GmArray rows)
            return;
        foreach (var row in rows.Items.OfType<GmArray>())
            for (var j = 0; j < row.Items.Count; j++)
                if (row.Items[j] is GmString s && s.Value == hotbarName)
                    row.Items[j] = GmNumber.From(-4);
    }

    private int IndexOf(string obj) => IndexOfOrNull(obj) ?? throw new EditRejectedException($"Unknown skill {obj}");

    private int? IndexOfOrNull(string obj)
    {
        var items = List.Items;
        for (var i = 0; i + Stride <= items.Count; i += Stride)
            if (items[i] is GmString s && s.Value == obj)
                return i;
        return null;
    }
}

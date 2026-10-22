using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;

namespace StoneshardSaveEditor.UI.Services;

public sealed record SetupView(string SavesRoot, bool SavesFound, string? GameDir, bool GameFound, string? GameError);

public sealed record SlotView(string Folder, string DisplayName, DateTime SavedAtLocal, string Location, SlotStatus Status, string? StatusMessage);

public sealed record CharacterView(string Folder, string Name, string? ClassName, int? Level, string AvatarSprite, IReadOnlyList<SlotView> Slots);

/// <param name="Locked">As the save has it; the game recomputes locks inside open trees when it loads.</param>
/// <param name="Available">Learnable under the game rules: everything it needs is learned (first tier: its tree is open).
/// Without rules loaded, the save's own flag.</param>
/// <param name="Requires">The skills this one needs, as object names (empty for the first tier or without a layout).</param>
public sealed record SkillView(
    string Object, string DisplayName, string Sprite, bool Passive, bool Learned, bool Locked, bool Available, int? Col, int? Row,
    IReadOnlyList<string> Requires);

public sealed record SkillTreeView(
    string Id, string DisplayName, string? TreatisePrefix, bool Open, int LearnedCount, bool CanUnlock, IReadOnlyList<SkillView> Skills);

public sealed record ReputationView(string Name, string LocationKey, double Value);

public sealed record SessionView(
    string CharacterFolder,
    string SlotFolder,
    string Name,
    string AvatarSprite,
    string Location,
    bool ReadOnly,
    string? ReadOnlyReason,
    int ChangeCount,
    bool GameFound,
    IReadOnlyDictionary<CharacterField, double> Character,
    int Gold,
    string? GoldNote,
    IReadOnlyList<ItemView> Items,
    int BagRows,
    IReadOnlyList<SkillTreeView> Trees,
    bool RulesAvailable,
    IReadOnlyList<ReputationView> Reputation);

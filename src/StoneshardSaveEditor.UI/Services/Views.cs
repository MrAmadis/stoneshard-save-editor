using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;

namespace StoneshardSaveEditor.UI.Services;

public sealed record SetupView(string SavesRoot, bool SavesFound, string? GameDir, bool GameFound, string? GameError);

public sealed record SlotView(string Folder, string DisplayName, DateTime SavedAtLocal, string Location, SlotStatus Status, string? StatusMessage);

public sealed record CharacterView(string Folder, string Name, string? ClassName, int? Level, string AvatarSprite, IReadOnlyList<SlotView> Slots);

public sealed record SkillView(string Object, string DisplayName, string Sprite, bool Passive, bool Learned, bool Locked, int? Col, int? Row);

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
    IReadOnlyList<ItemView> Items,
    int BagRows,
    IReadOnlyList<SkillTreeView> Trees,
    bool RulesAvailable,
    IReadOnlyList<ReputationView> Reputation);

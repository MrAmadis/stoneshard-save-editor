using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.GameData;

namespace StoneshardSaveEditor.UI.Services;

/// <param name="PickFolder">Shows a native folder dialog; supplied by the desktop window.</param>
public sealed record EditorOptions(
    string SavesRoot,
    string BackupRoot,
    string CacheRoot,
    string SettingsPath,
    IGameProcessProbe Probe,
    Func<string?>? PickFolder);

/// <summary>
/// Everything the screens do: list saves, open one, apply edits through the Core editors, save with a backup.
/// Every edit returns the new state and raises <see cref="Changed"/>; rejected edits throw
/// <see cref="EditRejectedException"/> with a message for the user.
/// </summary>
public sealed class EditorService(EditorOptions options, GameDataProvider game)
{
    private const string MoneybagKey = "o_inv_moneybag";

    private readonly Lock _lock = new();
    private readonly BackupService _backups = new(options.BackupRoot);
    private SaveSession? _session;
    private SkillCatalog? _skills;

    public event Action? Changed;

    public bool IsOpen => _session is not null;

    public SessionView Current
    {
        get
        {
            lock (_lock)
                return View(Session);
        }
    }

    private SaveSession Session => _session ?? throw new InvalidOperationException("No save is open");

    public SetupView Setup() =>
        new(options.SavesRoot, Directory.Exists(options.SavesRoot), game.GameDir, game.Files is not null, game.Error);

    public void SetGameDir(string path)
    {
        if (!GameInstall.IsGameDir(path))
            throw new EditRejectedException("That folder doesn't contain Stoneshard (data.win and StoneShard.exe)");
        var settings = Settings.Load(options.SettingsPath);
        settings.GameDir = path;
        settings.Save(options.SettingsPath);
        lock (_lock)
        {
            game.Reset();
            _skills = null;
        }
        Changed?.Invoke();
    }

    public string? BrowseForGameDir() => options.PickFolder?.Invoke();

    public IReadOnlyList<CharacterView> Characters()
    {
        var localization = game.Files?.Localization;
        return new SaveLocator(options.SavesRoot).Scan()
            .Select(c => new CharacterView(
                c.Folder,
                c.NameKey,
                c.ClassKey is null ? null : Names.Pretty(c.ClassKey, null),
                c.Level,
                c.AvatarSprite,
                c.Slots.Select(s => new SlotView(
                    s.SlotFolder, Names.SlotDisplayName(s.SlotFolder), s.SavedAtUtc.ToLocalTime(),
                    Names.Pretty(s.LocationKey, localization), s.Status, s.StatusMessage)).ToList()))
            .ToList();
    }

    public SessionView Open(string character, string slot)
    {
        var info = new SaveLocator(options.SavesRoot).Scan()
                       .FirstOrDefault(c => c.Folder == character)?.Slots.FirstOrDefault(s => s.SlotFolder == slot)
                   ?? throw new EditRejectedException("That save no longer exists");
        try
        {
            lock (_lock)
            {
                _session = SaveSession.Open(info);
                _skills = null;
            }
        }
        catch (SaveFormatException e)
        {
            throw new EditRejectedException($"This save can't be opened: {e.Message}");
        }
        Changed?.Invoke();
        return Current;
    }

    /// <summary>Goes back to the save list; unsaved edits are dropped.</summary>
    public void Close()
    {
        lock (_lock)
        {
            _session = null;
            _skills = null;
        }
        Changed?.Invoke();
    }

    public SessionView Reload()
    {
        var slot = Session.Slot;
        return Open(slot.CharacterFolder, slot.SlotFolder);
    }

    public SaveResult Save()
    {
        SaveResult result;
        lock (_lock)
            result = Session.Save(_backups, options.Probe);
        Changed?.Invoke();
        return result;
    }

    public IReadOnlyList<BackupInfo> Backups()
    {
        var slot = Session.Slot;
        return _backups.List(slot.CharacterFolder, slot.SlotFolder);
    }

    public SessionView Restore(string backupPath)
    {
        var backup = Backups().FirstOrDefault(b =>
                         string.Equals(Path.GetFullPath(b.Path), Path.GetFullPath(backupPath), StringComparison.OrdinalIgnoreCase))
                     ?? throw new EditRejectedException("That backup doesn't belong to this save");
        if (options.Probe.IsGameRunning())
            throw new EditRejectedException("Close Stoneshard before restoring a backup");
        _backups.Restore(backup, Session.Slot);
        return Reload();
    }

    public bool GameRunning() => options.Probe.IsGameRunning();

    public SessionView SetCharacter(CharacterField field, double value) => Edit(s => new CharacterEditor(s).Set(field, value));

    public SessionView SetReputation(string locationKey, double value) => Edit(s => new ReputationEditor(s).Set(locationKey, value));

    public SessionView SetCharges(int index, int value) => Edit(s => Inventory(s).SetCharges(index, value));

    public SessionView SetStack(int index, int value) => Edit(s => Inventory(s).SetStack(index, value));

    public SessionView SetDurability(int index, double current, double max) => Edit(s => Inventory(s).SetDurability(index, current, max));

    public SessionView DeleteItem(int index) => Edit(s => Inventory(s).Delete(index));

    public SessionView DuplicateItem(int index) => Edit(s =>
    {
        RequireGame();
        Inventory(s).Duplicate(index);
    });

    public SessionView AddItem(string key) => Edit(s => Inventory(s).Add(RequireGame().Catalog.CreateEntry(key, s.Root)));

    public SessionView SetGold(int amount) =>
        Edit(s => Inventory(s).SetGold(amount, () => RequireGame().Catalog.CreateEntry(MoneybagKey, s.Root)));

    /// <param name="rules">Follow game rules: the skill must be available and costs an ability point.</param>
    public SessionView LearnSkill(string obj, bool rules) => Edit(s =>
    {
        var skills = new SkillsEditor(s);
        if (rules && skills.Read().FirstOrDefault(x => x.Object == obj) is { Locked: true })
            throw new EditRejectedException("That skill isn't available yet");
        skills.Learn(obj, spendPoint: rules);
    });

    /// <param name="rules">Follow game rules: unlearning refunds an ability point.</param>
    public SessionView UnlearnSkill(string obj, bool rules) => Edit(s => new SkillsEditor(s).Unlearn(obj, refundPoint: rules));

    public SessionView RespecAll() => Edit(s => new SkillsEditor(s).RespecAll());

    public SessionView RefundTree(string treeId) =>
        Edit(s => new SkillsEditor(s).RefundTree(Tree(s, treeId).Skills.Select(k => k.Object)));

    /// <summary>Opens a closed tree the way reading its first treatise does (observed in-game).</summary>
    public SessionView UnlockTree(string treeId) => Edit(s =>
    {
        var tree = Tree(s, treeId);
        var firstTier = SkillsFor(s)!.UnlockSet(treeId);
        if (tree.TreatisePrefix is null || firstTier.Count == 0)
            throw new EditRejectedException($"There's no unlock data for {tree.DisplayName} yet");
        new SkillsEditor(s).UnlockTree(firstTier, $"treatise_{tree.TreatisePrefix}1");
    });

    public IReadOnlyList<CatalogItem> Catalog()
    {
        lock (_lock)
            return RequireGame().Catalog.All(Session.Root);
    }

    private SessionView Edit(Action<SaveSession> apply)
    {
        lock (_lock)
        {
            var session = Session;
            if (session.ReadOnly)
                throw new EditRejectedException($"This save is read-only: {session.ReadOnlyReason}");
            apply(session);
        }
        Changed?.Invoke();
        return Current;
    }

    private GameFiles RequireGame() => game.Files ?? throw new EditRejectedException("Game files are needed for this");

    private InventoryEditor Inventory(SaveSession s) => new(s, (IItemShapeProvider?)game.Files?.Shapes ?? UnknownShapes.Instance);

    private SkillCatalog? SkillsFor(SaveSession s)
    {
        if (_skills is not null || game.Files is not { } files)
            return _skills;
        try
        {
            return _skills = SkillCatalog.Build(s.Root, files.Tables, files.Localization, files.Objects);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private SkillTree Tree(SaveSession s, string treeId)
    {
        RequireGame();
        return SkillsFor(s)?.Trees.FirstOrDefault(t => t.Id == treeId) ?? throw new EditRejectedException($"Unknown skill tree {treeId}");
    }

    private SessionView View(SaveSession s)
    {
        var files = game.Files;
        var inventory = Inventory(s);
        var state = SkillsEditor.ReadFrom(s.Root).ToDictionary(x => x.Object);
        var catalog = SkillsFor(s);

        var trees = catalog?.Trees.Select(t =>
        {
            var layout = catalog.Layout(t.Id);
            var skills = t.Skills.Select(k =>
            {
                var current = state.GetValueOrDefault(k.Object);
                var at = layout?.Skills.GetValueOrDefault(k.Object);
                return new SkillView(k.Object, k.DisplayName, k.Sprite, k.Passive, current?.Learned ?? false, current?.Locked ?? true, at?.Col, at?.Row);
            }).ToList();
            var open = skills.Any(k => k.Learned || !k.Locked);
            return new SkillTreeView(t.Id, t.DisplayName, t.TreatisePrefix, open, skills.Count(k => k.Learned),
                !open && catalog.UnlockSet(t.Id).Count > 0, skills);
        }).ToList() ?? [];

        var rulesAvailable = catalog is not null
                             && catalog.Trees.Where(t => t.TreatisePrefix is not null).All(t => catalog.Layout(t.Id) is not null);
        var reputation = new ReputationEditor(s).Read()
            .Select(r => new ReputationView(Names.Pretty(r.VillageKey, null), r.LocationKey, r.Value))
            .ToList();

        return new SessionView(
            s.Slot.CharacterFolder, s.Slot.SlotFolder, s.Slot.NameKey, s.Slot.AvatarSprite, Names.Pretty(s.Slot.LocationKey, files?.Localization),
            s.ReadOnly, s.ReadOnlyReason, s.ChangeCount, files is not null,
            new CharacterEditor(s).Read(), inventory.Gold(), inventory.Read(), InventoryEditor.BagRows,
            trees, rulesAvailable, reputation);
    }

    /// <summary>Used when the game files can't be read: every item is 1×1 with no icon, and nothing gets placed.</summary>
    private sealed class UnknownShapes : IItemShapeProvider
    {
        public static readonly UnknownShapes Instance = new();

        public (int W, int H) Footprint(string itemName) => (1, 1);

        public string SpriteFor(string itemName) => GameItemShapes.Missing;
    }
}

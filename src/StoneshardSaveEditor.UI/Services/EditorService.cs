using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;
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
    private const string CoinKey = "o_inv_gold";

    private readonly Lock _lock = new();
    private readonly BackupService _backups = new(options.BackupRoot);
    private SaveSession? _session;
    private SkillCatalog? _skills;

    public event Action? Changed;

    public bool IsOpen
    {
        get
        {
            lock (_lock)
                return _session is not null;
        }
    }

    /// <summary>The open save's slot, read under the lock.</summary>
    private SlotInfo OpenSlot
    {
        get
        {
            lock (_lock)
                return Session.Slot;
        }
    }

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
        catch (Exception e) when (e is SaveFormatException or FormatException or IOException or InvalidDataException or UnauthorizedAccessException)
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
        var slot = OpenSlot;
        return Open(slot.CharacterFolder, slot.SlotFolder);
    }

    public SaveResult Save()
    {
        SaveResult result;
        try
        {
            lock (_lock)
                result = Session.Save(_backups, options.Probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new EditRejectedException($"Couldn't write the save: {e.Message}");
        }
        Changed?.Invoke();
        return result;
    }

    public IReadOnlyList<BackupInfo> Backups()
    {
        var slot = OpenSlot;
        return _backups.List(slot.CharacterFolder, slot.SlotFolder);
    }

    public SessionView Restore(string backupPath)
    {
        var backup = Backups().FirstOrDefault(b =>
                         string.Equals(Path.GetFullPath(b.Path), Path.GetFullPath(backupPath), StringComparison.OrdinalIgnoreCase))
                     ?? throw new EditRejectedException("That backup doesn't belong to this save");
        if (options.Probe.IsGameRunning())
            throw new EditRejectedException("Close Stoneshard before restoring a backup");
        try
        {
            _backups.Restore(backup, OpenSlot);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new EditRejectedException($"Couldn't restore that backup: {e.Message}");
        }
        return Reload();
    }

    public bool GameRunning() => options.Probe.IsGameRunning();

    public SessionView SetCharacter(CharacterField field, double value) => Edit(s => new CharacterEditor(s).Set(field, value));

    public SessionView SetReputation(string locationKey, double value) => Edit(s => new ReputationEditor(s).Set(locationKey, value));

    public SessionView SetCharges(int index, int value) => Edit(s => Inventory(s).SetCharges(index, value));

    /// <summary>Puts <paramref name="amount"/> gold into one purse as real coin stacks.</summary>
    public SessionView SetPurseGold(int index, int amount) => Edit(s =>
    {
        if (amount > 0)
            RequireGame();
        Inventory(s).SetPurseGold(index, amount, () => NewCoin(s));
    });

    public SessionView SetCount(int index, int value) => Edit(s => Inventory(s).SetCount(index, value));

    public SessionView SetDurability(int index, double current, double max) => Edit(s => Inventory(s).SetDurability(index, current, max));

    public SessionView DeleteItem(int index) => Edit(s => Inventory(s).Delete(index));

    public SessionView DuplicateItem(int index) => Edit(s =>
    {
        RequireGame();
        Inventory(s).Duplicate(index);
    });

    public SessionView AddItem(string key) => Edit(s => Inventory(s).Add(RequireGame().Catalog.CreateEntry(key, s.Root)));

    /// <summary>Sets the gold carried, as real coin stacks in purses (adding purses past 2,000).</summary>
    public SessionView SetGold(int amount) => Edit(s =>
    {
        if (amount > 0)
            RequireGame();
        Inventory(s).SetGold(amount, () => RequireGame().Catalog.CreateEntry(MoneybagKey, s.Root), () => NewCoin(s));
    });

    private GmArray NewCoin(SaveSession s) => RequireGame().Catalog.CreateEntry(CoinKey, s.Root);

    /// <param name="rules">Follow game rules: the skill must be available and costs an ability point.</param>
    public SessionView LearnSkill(string obj, bool rules) => Edit(s => new SkillsEditor(s).Learn(obj, rules ? RulesFor(s) : null));

    /// <param name="rules">Follow game rules: unlearning refunds an ability point and is refused while a learned skill needs it.</param>
    public SessionView UnlearnSkill(string obj, bool rules) => Edit(s =>
    {
        var skills = new SkillsEditor(s);
        var gameRules = rules ? RulesFor(s) : null;
        if (gameRules?.BlockingDependent(obj, skills.Read()) is { } dependent)
            throw new EditRejectedException($"Unlearn {SkillName(s, dependent)} first");
        skills.Unlearn(obj, gameRules);
    });

    /// <param name="rules">Follow game rules: afterwards only first-tier skills stay available.</param>
    public SessionView RespecAll(bool rules = false) => Edit(s =>
    {
        var skills = new SkillsEditor(s);
        skills.RespecAll();
        if (rules && RulesFor(s) is { } gameRules)
            skills.Lock(gameRules.ToRelockAfterRespec(skills.Read()));
    });

    public SessionView RefundTree(string treeId, bool rules = false) => Edit(s =>
    {
        var skills = new SkillsEditor(s);
        var inTree = Tree(s, treeId).Skills.Select(k => k.Object).ToHashSet();
        skills.RefundTree(inTree);
        if (rules && RulesFor(s) is { } gameRules)
            skills.Lock(gameRules.ToRelockAfterRespec(skills.Read()).Where(inTree.Contains));
    });

    /// <summary>Opens a closed tree the way reading its first treatise does (observed in-game).</summary>
    public SessionView UnlockTree(string treeId) => Edit(s =>
    {
        var tree = Tree(s, treeId);
        var firstTier = SkillsFor(s)!.UnlockSet(treeId);
        if (tree.TreatisePrefix is null || firstTier.Count == 0)
            throw new EditRejectedException($"There's no unlock data for {tree.DisplayName} yet");
        new SkillsEditor(s).UnlockTree(firstTier, $"treatise_{tree.TreatisePrefix}1");
    });

    /// <summary>The name players see for an inventory item ("o_inv_salve" → "Salve").</summary>
    public string ItemName(string name) =>
        game.Files?.Catalog.DisplayName(name) ?? Names.Pretty(name.StartsWith("o_inv_", StringComparison.Ordinal) ? name[6..] : name, null);

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
            session.Apply(() => apply(session));
        }
        Changed?.Invoke();
        return Current;
    }

    private GameFiles RequireGame() => game.Files ?? throw new EditRejectedException("Game files are needed for this");

    private InventoryEditor Inventory(SaveSession s) => new(s, (IItemShapeProvider?)game.Files?.Shapes ?? UnknownShapes.Instance);

    /// <summary>A warning when a purse's cached total isn't backed by coins: the game recounts it when the purse is opened.</summary>
    private static string? GoldNote(InventoryEditor inventory)
    {
        var stale = inventory.StalePurses();
        if (stale.Count == 0)
            return null;
        var (claimed, coins) = (stale.Sum(p => p.Claimed), stale.Sum(p => p.Coins));
        var purses = stale.Count == 1 ? "The purse says" : $"{stale.Count} purses say";
        return $"{purses} {claimed} gold but {(stale.Count == 1 ? "holds" : "hold")} {coins} in coins. "
               + $"The game shows {coins} once the purse is opened; set the gold here to put real coins in it.";
    }

    /// <summary>The game's skill prerequisites, or null while they aren't loaded (then rules are off).</summary>
    private SkillRules? RulesFor(SaveSession s) => SkillsFor(s)?.Rules();

    private string SkillName(SaveSession s, string obj) =>
        SkillsFor(s)?.Trees.SelectMany(t => t.Skills).FirstOrDefault(k => k.Object == obj)?.DisplayName ?? obj;

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
        var rules = catalog?.Rules();
        var stateList = state.Values.ToList();

        var trees = catalog?.Trees.Select(t =>
        {
            var layout = catalog.Layout(t.Id);
            var skills = t.Skills.Select(k =>
            {
                var current = state.GetValueOrDefault(k.Object);
                var at = layout?.Skills.GetValueOrDefault(k.Object);
                var learned = current?.Learned ?? false;
                var locked = current?.Locked ?? true;
                var available = !learned && (rules?.CanLearn(k.Object, stateList) ?? !locked);
                return new SkillView(k.Object, k.DisplayName, k.Sprite, k.Passive, learned, locked, available, at?.Col, at?.Row,
                    at?.Requires ?? []);
            }).ToList();
            var open = skills.Any(k => k.Learned || !k.Locked);
            return new SkillTreeView(t.Id, t.DisplayName, t.TreatisePrefix, open, skills.Count(k => k.Learned),
                !open && catalog.UnlockSet(t.Id).Count > 0, skills);
        }).ToList() ?? [];

        var rulesAvailable = rules is not null;
        var reputation = new ReputationEditor(s).Read()
            .Select(r => new ReputationView(Names.Pretty(r.VillageKey, null), r.LocationKey, r.Value))
            .ToList();

        return new SessionView(
            s.Slot.CharacterFolder, s.Slot.SlotFolder, s.Slot.NameKey, s.Slot.AvatarSprite, Names.Pretty(s.Slot.LocationKey, files?.Localization),
            s.ReadOnly, s.ReadOnlyReason, s.ChangeCount, files is not null,
            new CharacterEditor(s).Read(), inventory.Gold(), GoldNote(inventory), inventory.Read(), InventoryEditor.BagRows,
            trees, rulesAvailable, reputation);
    }

    /// <summary>Used when the game files can't be read: every item is 1×1 with no icon, nothing stacks, and nothing gets placed.</summary>
    private sealed class UnknownShapes : IItemShapeProvider
    {
        public static readonly UnknownShapes Instance = new();

        public (int W, int H) Footprint(string itemName) => (1, 1);

        public string SpriteFor(string itemName) => GameItemShapes.Missing;

        public int MaxStack(string itemName) => 0;
    }
}

using System.Text;
using System.Text.RegularExpressions;
using StoneshardSaveEditor.Core.Files;
using StoneshardSaveEditor.Core.Json;
using StoneshardSaveEditor.Lab;

// Developer tool for in-game experiments: test characters, inspecting and diffing saves.
// It only ever creates new character folders and rewrites characters.map (after a backup).

if (args.Length == 0)
    return Usage();

var options = ParseOptions(args.Skip(1).ToArray());
try
{
    return args[0] switch
    {
        "clone-test-character" => CloneTestCharacter(options),
        "point-continue" => PointContinue(options),
        "keep-save" => KeepSave(options),
        "reset-skills" => ResetSkills(options),
        "dump" => Dump(options),
        "diff" => DiffFiles(options),
        "make-experiments" => MakeExperiments(options),
        "make-session-b" => MakeSessionB(options),
        "make-session-c" => MakeSessionC(options),
        "make-session-d" => MakeSessionD(options),
        "make-session-g" => MakeSessionG(options),
        "make-session-h" => MakeSessionH(options),
        "extract-unlocks" => ExtractUnlocks(options),
        "watch" => WatchSaves(options),
        _ => Usage(),
    };
}
catch (Exception e) when (e is ArgumentException or SaveFormatException or IOException or FormatException or InvalidOperationException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}

static int MakeExperiments(Dictionary<string, string> o)
{
    var game = GameServices.Load(Required(o, "game"));
    var made = Experiments.Build(Required(o, "root"), game, new BackupService(BackupService.DefaultRoot), new GameProcessProbe());
    foreach (var m in made)
        Console.WriteLine($"{m.Folder}\t{m.Label}\t{m.Purpose}");
    return 0;
}

static int ExtractUnlocks(Dictionary<string, string> o)
{
    var before = (GmObject)Load(Required(o, "before"));
    var after = (GmObject)Load(Required(o, "after"));
    var skills = GameServices.Load(Required(o, "game")).SkillsFor(after);
    var unlocked = UnlockExtractor.NewlyUnlocked(before, after);
    var read = UnlockExtractor.NewlyReadTreatises(before, after);
    var grouped = UnlockExtractor.GroupByTreatise(unlocked, s => skills.TreeOf(s).TreatisePrefix, int.Parse(Required(o, "volume")), read);
    UnlockExtractor.Merge(Required(o, "json"), grouped);
    foreach (var (key, list) in grouped)
        Console.WriteLine($"{key}: {string.Join(", ", list)}");
    return 0;
}

static int WatchSaves(Dictionary<string, string> o)
{
    SnapshotStore.Watch(Required(o, "root"), Required(o, "out"));
    return 0;
}

static int MakeSessionB(Dictionary<string, string> o)
{
    var game = GameServices.Load(Required(o, "game"));
    var made = Experiments.BuildSessionB(Required(o, "root"), game, new BackupService(BackupService.DefaultRoot), new GameProcessProbe());
    foreach (var m in made)
        Console.WriteLine($"{m.Folder}\t{m.Label}\t{m.Purpose}");
    return 0;
}
static int MakeSessionH(Dictionary<string, string> o)
{
    var game = GameServices.Load(Required(o, "game"));
    var made = Experiments.BuildSessionH(Required(o, "root"), game, new BackupService(BackupService.DefaultRoot), new GameProcessProbe());
    foreach (var m in made)
        Console.WriteLine($"{m.Folder}	{m.Label}	{m.Purpose}");
    return 0;
}

static int MakeSessionG(Dictionary<string, string> o)
{
    var game = GameServices.Load(Required(o, "game"));
    var made = Experiments.BuildSessionG(Required(o, "root"), game, new BackupService(BackupService.DefaultRoot), new GameProcessProbe());
    foreach (var m in made)
        Console.WriteLine($"{m.Folder}	{m.Label}	{m.Purpose}");
    return 0;
}

static int MakeSessionD(Dictionary<string, string> o)
{
    var game = GameServices.Load(Required(o, "game"));
    var made = Experiments.BuildSessionD(Required(o, "root"), game, new BackupService(BackupService.DefaultRoot), new GameProcessProbe());
    foreach (var m in made)
        Console.WriteLine($"{m.Folder}	{m.Label}	{m.Purpose}");
    return 0;
}
static int MakeSessionC(Dictionary<string, string> o)
{
    var game = GameServices.Load(Required(o, "game"));
    var made = Experiments.BuildSessionC(Required(o, "root"), game, new BackupService(BackupService.DefaultRoot), new GameProcessProbe());
    foreach (var m in made)
        Console.WriteLine($"{m.Folder}	{m.Label}	{m.Purpose}");
    return 0;
}
static int CloneTestCharacter(Dictionary<string, string> o)
{
    var created = TestCharacterCloner.Clone(Required(o, "root"), Required(o, "from"), Required(o, "slot"), o.GetValueOrDefault("label"));
    Console.WriteLine(created);
    return 0;
}

static int KeepSave(Dictionary<string, string> o)
{
    if (new GameProcessProbe().IsGameRunning())
    {
        Console.Error.WriteLine("Close Stoneshard first");
        return 1;
    }
    var slot = TestCharacterCloner.KeepAsSave(Required(o, "root"), Required(o, "character"), Required(o, "slot"));
    Console.WriteLine($"{o["character"]}/{o["slot"]} copied to {slot}");
    return 0;
}

static int ResetSkills(Dictionary<string, string> o)
{
    var game = GameServices.Load(Required(o, "game"));
    var slots = Experiments.ResetSkills(Required(o, "root"), Required(o, "character"), game, new BackupService(BackupService.DefaultRoot), new GameProcessProbe());
    Console.WriteLine($"{o["character"]}: {string.Join(", ", slots)} reset (every treatise read, only starting skills, 100 ability points)");
    return 0;
}

static int PointContinue(Dictionary<string, string> o)
{
    TestCharacterCloner.PointContinue(Required(o, "root"), Required(o, "character"), Required(o, "slot"), BackupService.DefaultRoot);
    Console.WriteLine($"Continue now loads {o["character"]}/{o["slot"]}");
    return 0;
}

static int Dump(Dictionary<string, string> o)
{
    var node = Load(Required(o, "file"));
    if (o.TryGetValue("path", out var path))
        node = Navigate(node, path);
    Console.WriteLine(Encoding.UTF8.GetString(GmJson.Write(node)));
    return 0;
}

static int DiffFiles(Dictionary<string, string> o)
{
    var lines = GmDiff.Diff(Load(Required(o, "a")), Load(Required(o, "b")));
    foreach (var line in lines)
        Console.WriteLine(line);
    Console.Error.WriteLine($"{lines.Count} difference(s)");
    return 0;
}

static GmNode Load(string file) => GmJson.Parse(SaveCodec.Decode(File.ReadAllBytes(file)).Json);

static GmNode Navigate(GmNode node, string path)
{
    foreach (Match m in Regex.Matches(path, @"([^.\[\]]+)|\[(\d+)\]"))
    {
        node = m.Groups[1].Success
            ? (node as GmObject)?[m.Groups[1].Value] ?? throw new ArgumentException($"no key '{m.Groups[1].Value}' in path {path}")
            : node is GmArray a && int.Parse(m.Groups[2].Value) is var i && i < a.Items.Count
                ? a.Items[i]
                : throw new ArgumentException($"index [{m.Groups[2].Value}] out of range in path {path}");
    }
    return node;
}

static Dictionary<string, string> ParseOptions(string[] rest)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i + 1 < rest.Length; i += 2)
    {
        if (!rest[i].StartsWith("--"))
            throw new ArgumentException($"expected an option, got '{rest[i]}'");
        options[rest[i][2..]] = rest[i + 1];
    }
    return options;
}

static string Required(Dictionary<string, string> o, string name) =>
    o.TryGetValue(name, out var v) ? v : throw new ArgumentException($"missing --{name}");

static int Usage()
{
    Console.Error.WriteLine("""
        usage:
          clone-test-character --root <characters_v1> --from <character_N> --slot <slot> [--label <locationKey>]
          point-continue       --root <characters_v1> --character <character_N> --slot <slot>
          keep-save            --root <characters_v1> --character <character_N> --slot <slot>
          reset-skills         --root <characters_v1> --character <character_N> --game <Stoneshard folder>
          dump                 --file <save file> [--path a.b[3]]
          diff                 --a <data.sav> --b <data.sav>
          make-experiments     --root <characters_v1> --game <Stoneshard folder>
          make-session-b       --root <characters_v1> --game <Stoneshard folder>
          make-session-c       --root <characters_v1> --game <Stoneshard folder>
          make-session-d       --root <characters_v1> --game <Stoneshard folder>
          make-session-g       --root <characters_v1> --game <Stoneshard folder>
          make-session-h       --root <characters_v1> --game <Stoneshard folder>
          extract-unlocks      --before <data.sav> --after <data.sav> --volume <n> --game <Stoneshard folder> --json <tree-unlocks.json>
          watch                --root <characters_v1> --out <folder>
        """);
    return 2;
}

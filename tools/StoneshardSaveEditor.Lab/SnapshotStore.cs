using System.Globalization;

namespace StoneshardSaveEditor.Lab;

/// <summary>Keeps a timestamped copy of every save the game writes for a test character during an in-game session.</summary>
public static class SnapshotStore
{
    /// <summary>The owner's real characters; never snapshotted (and never written by the Lab).</summary>
    public static readonly IReadOnlySet<string> RealCharacters = new HashSet<string> { "character_1", "character_2" };

    public static string? Capture(string root, string changedFile, string outDir)
    {
        var relative = Path.GetRelativePath(root, changedFile).Split(Path.DirectorySeparatorChar);
        if (relative.Length != 3 || relative[2] != "data.sav" || !relative[0].StartsWith("character_", StringComparison.Ordinal)
            || RealCharacters.Contains(relative[0]))
            return null;

        var dir = Path.Combine(outDir, relative[0]);
        Directory.CreateDirectory(dir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var target = Path.Combine(dir, $"{relative[1]}-{stamp}-data.sav");
        File.Copy(changedFile, target, overwrite: true);
        return target;
    }

    /// <summary>Watches the saves folder until the process is stopped.</summary>
    public static void Watch(string root, string outDir)
    {
        foreach (var file in Directory.EnumerateFiles(root, "data.sav", SearchOption.AllDirectories))
            Report(Capture(root, file, outDir));

        var pending = new Dictionary<string, DateTime>();
        using var watcher = new FileSystemWatcher(root, "data.sav")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        void Queue(string path)
        {
            lock (pending)
                pending[path] = DateTime.UtcNow;
        }
        watcher.Changed += (_, e) => Queue(e.FullPath);
        watcher.Created += (_, e) => Queue(e.FullPath);
        watcher.Renamed += (_, e) => Queue(e.FullPath);
        watcher.EnableRaisingEvents = true;
        Console.WriteLine($"Watching {root} for test-character saves; copies go to {outDir}. Stop with Ctrl+C.");

        while (true)
        {
            Thread.Sleep(500);
            List<string> ready;
            lock (pending)
            {
                // Wait until the game has finished writing: no events for two seconds.
                ready = pending.Where(p => DateTime.UtcNow - p.Value > TimeSpan.FromSeconds(2)).Select(p => p.Key).ToList();
                foreach (var path in ready)
                    pending.Remove(path);
            }
            foreach (var path in ready)
            {
                try
                {
                    Report(Capture(root, path, outDir));
                }
                catch (IOException)
                {
                    Queue(path);
                }
            }
        }
    }

    private static void Report(string? copy)
    {
        if (copy is not null)
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} saved {copy}");
    }
}

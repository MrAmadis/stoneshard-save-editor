using System.IO.Compression;

namespace StoneshardSaveEditor.Tests;

/// <summary>Locates the git-ignored real-save snapshot and the local game install; skips tests when absent.</summary>
public static class Fixtures
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string RealSavesRoot
    {
        get
        {
            var root = Path.Combine(RepoRoot, "tests", "fixtures", "real", "characters_v1");
            if (!Directory.Exists(root))
                Assert.Skip($"Real save snapshot not found at {root}");
            return root;
        }
    }

    public static string GameDir
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("STONESHARD_DIR")
                      ?? @"D:\SteamLibrary\steamapps\common\Stoneshard";
            if (!File.Exists(Path.Combine(dir, "data.win")))
                Assert.Skip($"Stoneshard install not found at {dir}");
            return dir;
        }
    }

    /// <summary>Copies the real-save snapshot into a fresh temp folder and returns its characters_v1 path.</summary>
    public static string CopyRealSavesToTemp()
    {
        var source = RealSavesRoot;
        var target = Path.Combine(Path.GetTempPath(), "sse-tests", Guid.NewGuid().ToString("N"), "characters_v1");
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        return target;
    }

    /// <summary>Inflates a save file and strips the 32-char hash plus NUL trailer.</summary>
    public static byte[] DecompressedJson(string file)
    {
        using var input = new ZLibStream(File.OpenRead(file), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        var payload = output.ToArray();
        return payload[..^33];
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
}

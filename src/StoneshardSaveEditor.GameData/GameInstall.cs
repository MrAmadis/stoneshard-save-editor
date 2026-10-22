using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace StoneshardSaveEditor.GameData;

/// <summary>Finds the Stoneshard install: the configured folder first, then every Steam library.</summary>
public static partial class GameInstall
{
    public static string? Find(string? configuredDir)
    {
        if (!string.IsNullOrWhiteSpace(configuredDir) && IsGameDir(configuredDir))
            return configuredDir;
        return SteamLibraries()
            .Select(lib => Path.Combine(lib, "steamapps", "common", "Stoneshard"))
            .FirstOrDefault(IsGameDir);
    }

    public static bool IsGameDir(string dir) =>
        File.Exists(Path.Combine(dir, "data.win")) && File.Exists(Path.Combine(dir, "StoneShard.exe"));

    private static IEnumerable<string> SteamLibraries()
    {
        var steam = SteamPath();
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        var libraries = new List<string> { steam };
        if (File.Exists(vdf))
        {
            foreach (Match m in LibraryPath().Matches(File.ReadAllText(vdf)))
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string SteamPath()
    {
        if (OperatingSystem.IsWindows()
            && Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string path
            && Directory.Exists(path))
            return path.Replace('/', '\\');
        return @"C:\Program Files (x86)\Steam";
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPath();
}

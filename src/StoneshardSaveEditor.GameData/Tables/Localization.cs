using System.Text;

namespace StoneshardSaveEditor.GameData;

/// <summary>
/// English display names from the game's text tables: rows of <c>key;Русский;English;…</c> with 14 cells.
/// When a key appears more than once (name, then description), the first row wins.
/// </summary>
public sealed class Localization
{
    private const int Separators = 13;
    private const int EnglishColumn = 2;

    private readonly Dictionary<string, string> _english;

    private Localization(Dictionary<string, string> english) => _english = english;

    public static Localization Load(string exePath) => Parse(File.ReadAllBytes(exePath));

    public static Localization Parse(byte[] exe)
    {
        var english = new Dictionary<string, string>(StringComparer.Ordinal);
        var span = exe.AsSpan();
        var pos = 0;
        while (pos < span.Length)
        {
            var len = span[pos..].IndexOf((byte)0);
            if (len < 0)
                len = span.Length - pos;
            var segment = span.Slice(pos, len);
            pos += len + 1;

            if (segment.Length < Separators + 2 || segment[0] == (byte)';' || segment.Count((byte)';') != Separators)
                continue;
            var cells = Encoding.UTF8.GetString(segment).Split(';');
            if (cells[EnglishColumn].Length > 0)
                english.TryAdd(cells[0], cells[EnglishColumn]);
        }
        return new Localization(english);
    }

    public string? English(string key) => _english.GetValueOrDefault(key);
}

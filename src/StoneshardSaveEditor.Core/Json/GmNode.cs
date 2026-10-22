using System.Globalization;
using System.Text;

namespace StoneshardSaveEditor.Core.Json;

/// <summary>
/// A JSON value as GameMaker writes it. Numbers and strings keep their original token text so that
/// untouched values are written back byte for byte.
/// </summary>
public abstract class GmNode;

public sealed class GmObject : GmNode
{
    private readonly List<KeyValuePair<string, GmNode>> _entries = [];
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    public IReadOnlyList<KeyValuePair<string, GmNode>> Entries => _entries;

    /// <summary>Looks up a value by its raw key text (as written between the quotes).</summary>
    public GmNode? this[string key] => _index.TryGetValue(key, out var i) ? _entries[i].Value : null;

    /// <summary>Replaces the value of an existing key in place, or appends the key at the end.</summary>
    public void Set(string key, GmNode value)
    {
        if (_index.TryGetValue(key, out var i))
            _entries[i] = new(key, value);
        else
        {
            _index[key] = _entries.Count;
            _entries.Add(new(key, value));
        }
    }

    public bool Remove(string key)
    {
        if (!_index.TryGetValue(key, out var i))
            return false;
        _entries.RemoveAt(i);
        _index.Clear();
        for (var j = 0; j < _entries.Count; j++)
            _index.TryAdd(_entries[j].Key, j);
        return true;
    }

    internal void AddParsed(string key, GmNode value)
    {
        _index.TryAdd(key, _entries.Count);
        _entries.Add(new(key, value));
    }
}

public sealed class GmArray : GmNode
{
    public List<GmNode> Items { get; } = [];
}

public sealed class GmString : GmNode
{
    private string? _value;

    internal GmString(string raw) => Raw = raw;

    /// <summary>The escaped content between the quotes, exactly as stored in the file.</summary>
    public string Raw { get; }

    public string Value => _value ??= Unescape(Raw);

    public static GmString From(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '/': sb.Append("\\/"); break;
                case < ' ' or > '~': sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); break;
                default: sb.Append(c); break;
            }
        }
        return new GmString(sb.ToString()) { _value = value };
    }

    private static string Unescape(string raw)
    {
        if (!raw.Contains('\\'))
            return raw;
        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }
            var e = raw[++i];
            switch (e)
            {
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    sb.Append((char)int.Parse(raw.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default: sb.Append(e); break;
            }
        }
        return sb.ToString();
    }
}

public sealed class GmNumber : GmNode
{
    internal GmNumber(string raw) => Raw = raw;

    /// <summary>The number exactly as written in the file.</summary>
    public string Raw { get; }

    public double Value => double.Parse(Raw, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Formats a new value the way GameMaker writes reals: always with a decimal point.</summary>
    public static GmNumber From(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "GameMaker JSON cannot store NaN or infinity");
        if (value == 0)
            value = 0; // drop negative zero
        return new GmNumber(value.ToString("0.0###############", CultureInfo.InvariantCulture));
    }
}

public sealed class GmBool(bool value) : GmNode
{
    public bool Value { get; } = value;
}

public sealed class GmNull : GmNode;

public static class GmNodeExtensions
{
    /// <summary>GameMaker truthiness: <c>true</c>, or any number other than 0.</summary>
    public static bool IsTruthy(this GmNode node) => node switch
    {
        GmBool b => b.Value,
        GmNumber n => n.Value != 0,
        _ => false,
    };

    public static double AsDouble(this GmNode node) => node switch
    {
        GmBool b => b.Value ? 1 : 0,
        GmNumber n => n.Value,
        _ => throw new InvalidOperationException($"Expected a number or bool, found {node.GetType().Name}"),
    };

    public static GmNode DeepClone(this GmNode node)
    {
        switch (node)
        {
            case GmObject o:
                var obj = new GmObject();
                foreach (var (k, v) in o.Entries)
                    obj.AddParsed(k, v.DeepClone());
                return obj;
            case GmArray a:
                var arr = new GmArray();
                arr.Items.AddRange(a.Items.Select(DeepClone));
                return arr;
            case GmString s: return new GmString(s.Raw);
            case GmNumber n: return new GmNumber(n.Raw);
            case GmBool b: return new GmBool(b.Value);
            default: return new GmNull();
        }
    }
}

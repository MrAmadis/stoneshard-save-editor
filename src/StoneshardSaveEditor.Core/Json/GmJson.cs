using System.Text;

namespace StoneshardSaveEditor.Core.Json;

/// <summary>
/// Reads and writes the JSON dialect in Stoneshard saves. The writer reproduces GameMaker's layout exactly:
/// <c>{ "k": v, "k2": v }</c>, <c>[ a, b ]</c>, <c>{ }</c> and <c>[ ]</c> when empty, no newlines.
/// </summary>
public static class GmJson
{
    public static GmNode Parse(ReadOnlySpan<byte> utf8)
    {
        var reader = new Reader(utf8);
        var node = reader.ReadValue();
        reader.SkipWhitespace();
        if (!reader.AtEnd)
            throw reader.Error("unexpected content after the root value");
        return node;
    }

    public static byte[] Write(GmNode node)
    {
        var sb = new StringBuilder();
        WriteNode(sb, node);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void WriteNode(StringBuilder sb, GmNode node)
    {
        switch (node)
        {
            case GmObject o:
                if (o.Entries.Count == 0)
                {
                    sb.Append("{ }");
                    break;
                }
                sb.Append("{ ");
                for (var i = 0; i < o.Entries.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append('"').Append(o.Entries[i].Key).Append("\": ");
                    WriteNode(sb, o.Entries[i].Value);
                }
                sb.Append(" }");
                break;
            case GmArray a:
                if (a.Items.Count == 0)
                {
                    sb.Append("[ ]");
                    break;
                }
                sb.Append("[ ");
                for (var i = 0; i < a.Items.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    WriteNode(sb, a.Items[i]);
                }
                sb.Append(" ]");
                break;
            case GmString s: sb.Append('"').Append(s.Raw).Append('"'); break;
            case GmNumber n: sb.Append(n.Raw); break;
            case GmBool b: sb.Append(b.Value ? "true" : "false"); break;
            case GmNull: sb.Append("null"); break;
            default: throw new InvalidOperationException($"Unknown node type {node.GetType().Name}");
        }
    }

    private ref struct Reader(ReadOnlySpan<byte> s)
    {
        private readonly ReadOnlySpan<byte> _s = s;
        private int _pos;

        public readonly bool AtEnd => _pos >= _s.Length;

        public readonly FormatException Error(string message) => new($"Invalid save JSON at byte {_pos}: {message}");

        public void SkipWhitespace()
        {
            while (_pos < _s.Length && _s[_pos] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                _pos++;
        }

        public GmNode ReadValue()
        {
            SkipWhitespace();
            if (AtEnd)
                throw Error("unexpected end of input");
            return _s[_pos] switch
            {
                (byte)'{' => ReadObject(),
                (byte)'[' => ReadArray(),
                (byte)'"' => new GmString(ReadStringRaw()),
                (byte)'t' => ReadLiteral("true"u8, new GmBool(true)),
                (byte)'f' => ReadLiteral("false"u8, new GmBool(false)),
                (byte)'n' => ReadLiteral("null"u8, new GmNull()),
                (byte)'-' or (>= (byte)'0' and <= (byte)'9') => ReadNumber(),
                _ => throw Error($"unexpected character '{(char)_s[_pos]}'"),
            };
        }

        private GmObject ReadObject()
        {
            var obj = new GmObject();
            _pos++; // {
            SkipWhitespace();
            if (Peek() == '}')
            {
                _pos++;
                return obj;
            }
            while (true)
            {
                SkipWhitespace();
                if (Peek() != '"')
                    throw Error("expected a key");
                var key = ReadStringRaw();
                SkipWhitespace();
                Expect(':');
                obj.AddParsed(key, ReadValue());
                SkipWhitespace();
                var c = Peek();
                _pos++;
                if (c == '}') return obj;
                if (c != ',') throw Error("expected ',' or '}'");
            }
        }

        private GmArray ReadArray()
        {
            var arr = new GmArray();
            _pos++; // [
            SkipWhitespace();
            if (Peek() == ']')
            {
                _pos++;
                return arr;
            }
            while (true)
            {
                arr.Items.Add(ReadValue());
                SkipWhitespace();
                var c = Peek();
                _pos++;
                if (c == ']') return arr;
                if (c != ',') throw Error("expected ',' or ']'");
            }
        }

        private string ReadStringRaw()
        {
            _pos++; // opening quote
            var start = _pos;
            while (true)
            {
                if (AtEnd) throw Error("unterminated string");
                var b = _s[_pos];
                if (b == '"') break;
                _pos += b == '\\' ? 2 : 1;
            }
            var raw = Encoding.UTF8.GetString(_s[start.._pos]);
            _pos++; // closing quote
            return raw;
        }

        private GmNumber ReadNumber()
        {
            var start = _pos;
            if (_s[_pos] == '-') _pos++;
            var digits = SkipDigits();
            if (_pos < _s.Length && _s[_pos] == '.')
            {
                _pos++;
                digits &= SkipDigits();
            }
            if (_pos < _s.Length && _s[_pos] is (byte)'e' or (byte)'E')
            {
                _pos++;
                if (_pos < _s.Length && _s[_pos] is (byte)'+' or (byte)'-') _pos++;
                digits &= SkipDigits();
            }
            if (!digits) throw Error("malformed number");
            return new GmNumber(Encoding.ASCII.GetString(_s[start.._pos]));
        }

        private bool SkipDigits()
        {
            var start = _pos;
            while (_pos < _s.Length && _s[_pos] is >= (byte)'0' and <= (byte)'9') _pos++;
            return _pos > start;
        }

        private GmNode ReadLiteral(ReadOnlySpan<byte> literal, GmNode node)
        {
            if (!_s[_pos..].StartsWith(literal))
                throw Error("unknown literal");
            _pos += literal.Length;
            return node;
        }

        private readonly char Peek() => AtEnd ? '\0' : (char)_s[_pos];

        private void Expect(char c)
        {
            if (Peek() != c) throw Error($"expected '{c}'");
            _pos++;
        }
    }
}

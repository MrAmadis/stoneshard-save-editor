namespace StoneshardSaveEditor.GameData;

/// <summary>One row of a game data table; cells are looked up by header name.</summary>
public sealed class TableRow
{
    private readonly IReadOnlyDictionary<string, int> _columns;

    internal TableRow(IReadOnlyDictionary<string, int> columns, string[] raw)
    {
        _columns = columns;
        Raw = raw;
    }

    public string[] Raw { get; }

    public string Key => Raw[0];

    /// <summary>The table's column names, with blank or repeated headers named <c>#index</c>.</summary>
    public IEnumerable<string> Columns => _columns.Keys;

    /// <summary>The cell under <paramref name="column"/>, or an empty string when the column or cell is absent.</summary>
    public string this[string column] =>
        _columns.TryGetValue(column, out var i) && i < Raw.Length ? Raw[i] : "";
}

/// <summary>A semicolon-separated table the game keeps as string constants (weapons, armor, items, skills).</summary>
public sealed class GameTable
{
    private readonly Dictionary<string, TableRow> _byKey = new(StringComparer.Ordinal);

    internal GameTable(string[] header, IEnumerable<string[]> rows)
    {
        Header = header;
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++)
        {
            var name = header[i].Length == 0 || columns.ContainsKey(header[i]) ? $"#{i}" : header[i];
            columns[name] = i;
        }
        Rows = rows.Select(r => new TableRow(columns, r)).ToList();
        foreach (var row in Rows)
            _byKey.TryAdd(row.Key, row);
    }

    public string[] Header { get; }

    public IReadOnlyList<TableRow> Rows { get; }

    public TableRow? Find(string key) => _byKey.GetValueOrDefault(key);
}

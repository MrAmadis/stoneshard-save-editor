namespace StoneshardSaveEditor.Core.Editing;

/// <summary>Which cells of the backpack are taken; finds the first gap an item fits in.</summary>
internal sealed class BagGrid(int columns, int rows)
{
    private readonly bool[,] _taken = new bool[columns, rows];

    public void Occupy(int x, int y, int w, int h)
    {
        for (var cx = Math.Max(x, 0); cx < Math.Min(x + w, columns); cx++)
        for (var cy = Math.Max(y, 0); cy < Math.Min(y + h, rows); cy++)
            _taken[cx, cy] = true;
    }

    /// <summary>Scans rows top to bottom, then columns left to right.</summary>
    public (int X, int Y)? FirstFit(int w, int h)
    {
        for (var y = 0; y + h <= rows; y++)
        for (var x = 0; x + w <= columns; x++)
            if (Fits(x, y, w, h))
                return (x, y);
        return null;
    }

    private bool Fits(int x, int y, int w, int h)
    {
        for (var cx = x; cx < x + w; cx++)
        for (var cy = y; cy < y + h; cy++)
            if (_taken[cx, cy])
                return false;
        return true;
    }
}

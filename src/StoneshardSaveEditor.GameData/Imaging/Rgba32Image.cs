namespace StoneshardSaveEditor.GameData;

/// <summary>Uncompressed pixels, 4 bytes per pixel in R, G, B, A order, rows top to bottom.</summary>
public sealed record Rgba32Image(int Width, int Height, byte[] Pixels);

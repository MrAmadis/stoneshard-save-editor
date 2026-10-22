namespace StoneshardSaveEditor.Core.Files;

/// <summary>Save timestamps are days since 1899-12-30 (UTC), as GameMaker's date functions use.</summary>
public static class GameDate
{
    private static readonly DateTime Epoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Utc);

    public static DateTime ToUtc(double days) => Epoch.AddTicks((long)Math.Round(days * TimeSpan.TicksPerDay));
}

public static class SupportedVersions
{
    /// <summary>The save format version this editor was built and verified against.</summary>
    public const string WipeVersion = "0.9";
}

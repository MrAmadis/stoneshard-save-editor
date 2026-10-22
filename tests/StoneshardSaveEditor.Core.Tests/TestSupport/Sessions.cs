using StoneshardSaveEditor.Core.Files;

namespace StoneshardSaveEditor.Tests;

/// <summary>Opens a save session on a fresh temp copy of the real snapshot.</summary>
public static class Sessions
{
    public static SaveSession OpenVelmir() => Open("character_2", "exitsave_1");

    public static SaveSession Open(string character, string slot)
    {
        var root = Fixtures.CopyRealSavesToTemp();
        var info = new SaveLocator(root).Scan().Single(c => c.Folder == character).Slots.Single(s => s.SlotFolder == slot);
        return SaveSession.Open(info);
    }
}

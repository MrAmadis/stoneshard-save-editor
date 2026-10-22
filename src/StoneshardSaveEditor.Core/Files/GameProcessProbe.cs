using System.Diagnostics;

namespace StoneshardSaveEditor.Core.Files;

public interface IGameProcessProbe
{
    bool IsGameRunning();
}

/// <summary>Detects a running <c>StoneShard.exe</c>, which would overwrite edited saves when it exits.</summary>
public sealed class GameProcessProbe : IGameProcessProbe
{
    public bool IsGameRunning()
    {
        var processes = Process.GetProcessesByName("StoneShard");
        foreach (var p in processes)
            p.Dispose();
        return processes.Length > 0;
    }
}

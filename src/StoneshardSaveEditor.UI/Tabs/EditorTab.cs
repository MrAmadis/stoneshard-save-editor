using Microsoft.AspNetCore.Components;
using StoneshardSaveEditor.Core.Editing;
using StoneshardSaveEditor.UI.Services;

namespace StoneshardSaveEditor.UI.Tabs;

/// <summary>A tab of the editor: shows the open save and keeps the message of the last rejected edit.</summary>
public abstract class EditorTab : ComponentBase
{
    [Inject] protected EditorService EditorService { get; set; } = default!;

    [Parameter, EditorRequired] public SessionView Session { get; set; } = default!;

    /// <summary>Why the last edit was refused; cleared by the next edit that succeeds.</summary>
    protected string? Error { get; private set; }

    protected void Edit(Action edit)
    {
        try
        {
            edit();
            Error = null;
        }
        catch (EditRejectedException e)
        {
            Error = e.Message;
        }
    }
}

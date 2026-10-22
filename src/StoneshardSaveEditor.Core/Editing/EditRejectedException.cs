namespace StoneshardSaveEditor.Core.Editing;

/// <summary>An edit the editor refused; the message is shown to the user as is.</summary>
public sealed class EditRejectedException(string message) : Exception(message);

internal static class Limits
{
    public static void Check(string label, double value, double min, double max, bool whole)
    {
        if (!double.IsFinite(value) || value < min || value > max || (whole && value != Math.Floor(value)))
            throw new EditRejectedException(whole
                ? $"{label} must be a whole number between {min:0} and {max:0}"
                : $"{label} must be between {min:0} and {max:0}");
    }
}

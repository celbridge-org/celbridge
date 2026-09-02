using Microsoft.UI.Xaml.Controls;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Keeps a single-line TextBox free of the characters it can never hold a useful value for.
/// </summary>
public static class SingleLineText
{
    /// <summary>
    /// Removes tabs and line breaks from the text box, leaving the caret where the cleaned text puts it.
    /// Returns whether anything was removed. The Skia TextBox inserts a literal tab on Tab even when the
    /// key is handled, so a single-line box calls this from TextChanged to drop it while Tab still moves
    /// focus. Re-assigning Text re-enters TextChanged with the cleaned value.
    /// </summary>
    public static bool RemoveTabsAndLineBreaks(TextBox textBox)
    {
        var cleaned = textBox.Text
            .Replace("\t", string.Empty)
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty);

        if (cleaned == textBox.Text)
        {
            return false;
        }

        var caret = Math.Min(textBox.SelectionStart, cleaned.Length);
        textBox.Text = cleaned;
        textBox.SelectionStart = caret;

        return true;
    }
}

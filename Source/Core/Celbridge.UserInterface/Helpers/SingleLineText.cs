using Microsoft.UI.Xaml.Controls;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Keeps a single-line TextBox free of the characters it can never hold a useful value for.
/// </summary>
public static class SingleLineText
{
    /// <summary>
    /// The text with tabs and line breaks removed, and where the caret sits in it.
    /// </summary>
    public record Cleaned(string Text, int Caret);

    /// <summary>
    /// Removes tabs and line breaks from the given text, clamping the caret to the result.
    /// </summary>
    public static Cleaned Clean(string text, int caret)
    {
        var cleaned = text
            .Replace("\t", string.Empty)
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty);

        return new Cleaned(cleaned, Math.Clamp(caret, 0, cleaned.Length));
    }

    /// <summary>
    /// Removes tabs and line breaks from the text box, leaving the caret where the cleaned text puts it.
    /// Returns whether anything was removed. The Skia TextBox inserts a literal tab on Tab even when the
    /// key is handled, so a single-line box calls this from TextChanged to drop it while Tab still moves
    /// focus. Re-assigning Text re-enters TextChanged with the cleaned value.
    /// </summary>
    public static bool RemoveTabsAndLineBreaks(TextBox textBox)
    {
        var cleaned = Clean(textBox.Text, textBox.SelectionStart);
        if (cleaned.Text == textBox.Text)
        {
            return false;
        }

        textBox.Text = cleaned.Text;
        textBox.SelectionStart = cleaned.Caret;

        return true;
    }
}

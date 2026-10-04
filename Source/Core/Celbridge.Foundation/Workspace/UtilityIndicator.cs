using Celbridge.Documents;

namespace Celbridge.Workspace;

/// <summary>
/// The tone a utility can mark its rail button with, to show a state the user should stay aware of while the
/// utility is out of sight: a recorder that is recording, a watcher that has found a problem.
/// </summary>
public enum UtilityIndicatorTone
{
    /// <summary>
    /// No indicator: the button draws as normal.
    /// </summary>
    None,

    /// <summary>
    /// Red, for something live the user should know is happening, such as recording.
    /// </summary>
    Danger,

    /// <summary>
    /// Amber, for something that needs attention soon.
    /// </summary>
    Caution,

    /// <summary>
    /// Green, for something running as it should.
    /// </summary>
    Success,

    /// <summary>
    /// The accent colour, for something worth noticing that is neither good nor bad.
    /// </summary>
    Accent
}

public static class UtilityIndicatorTones
{
    /// <summary>
    /// Parses the wire name of a tone ("none", "danger", "caution", "success", "accent"), ignoring case. A blank
    /// name is None.
    /// </summary>
    public static bool TryParse(string? name, out UtilityIndicatorTone tone)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            tone = UtilityIndicatorTone.None;
            return true;
        }

        return Enum.TryParse(name.Trim(), ignoreCase: true, out tone) &&
            Enum.IsDefined(tone);
    }
}

/// <summary>
/// Sent when a utility sets or clears the indicator on its rail button. Label is a short state word appended
/// to the button's tooltip ("Recording"), empty for none. IconName replaces the button's glyph while the
/// indicator is on (a filled variant reads better in colour than an outline), empty to keep it.
/// </summary>
public record UtilityIndicatorChangedMessage(EditorId UtilityId, UtilityIndicatorTone Tone, string Label, string IconName = "");

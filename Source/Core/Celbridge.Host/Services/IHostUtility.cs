using StreamJsonRpc;

namespace Celbridge.Host;

public static class UtilityRpcMethods
{
    public const string SetIndicator = "utility/setIndicator";
}

/// <summary>
/// The contract a utility editor has with its rail button. A utility is often out of sight in the Utility
/// Panel or behind another tab, so a state the user must stay aware of (recording, a found problem) is shown
/// on the button that is always on screen.
/// </summary>
public interface IHostUtility
{
    /// <summary>
    /// Marks the utility's rail button with a tone ("danger", "caution", "success", "accent"), or clears it
    /// with "none" or no tone. Label is a short state word added to the button's tooltip. Icon is a prefixed
    /// icon name shown in place of the manifest's while the indicator is on, or empty to keep it. Turning a
    /// tone on plays a brief flash on the button. Refused for an editor that is not a utility.
    /// </summary>
    [JsonRpcMethod(UtilityRpcMethods.SetIndicator)]
    void SetIndicator(string? tone = null, string? label = null, string? icon = null);
}

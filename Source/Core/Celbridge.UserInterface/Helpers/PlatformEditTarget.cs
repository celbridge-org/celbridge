using Celbridge.Workspace;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// The edit target for a web page the host does not edit. The platform edits the page's own fields, so every
/// edit verb is left to the platform.
/// </summary>
public sealed class PlatformEditTarget : IEditTarget
{
    // The platform's own clipboard serves the page, so the host stands aside for the clipboard verbs rather
    // than swallowing them as unavailable.
    public bool HostMediatedClipboard => false;

    public bool HasPlatformEditing => true;

    public bool CanPerformEdit(EditIntent intent)
    {
        return false;
    }

    public void PerformEdit(EditIntent intent)
    {
    }

    public bool TryHandleTabKey(bool shift)
    {
        return false;
    }
}

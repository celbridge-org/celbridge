using Celbridge.Workspace;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// The edit target for a surface the host cannot edit, such as a web page or a settings form.
/// </summary>
public sealed class DisabledEditTarget : IEditTarget
{
    // The platform's own clipboard serves this surface, so the host stands aside for the clipboard verbs
    // rather than swallowing them as unavailable.
    public bool HostMediatedClipboard => false;

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

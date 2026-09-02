using Celbridge.Workspace;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// The edit target for a surface the host cannot edit, such as a web page or a settings form.
/// </summary>
public sealed class DisabledEditTarget : IEditTarget
{
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

using Celbridge.Workspace;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// The edit target for a surface built from managed controls that performs no edit verbs itself, such as a
/// settings form. A focused text control inside it still takes the verbs it can perform.
/// </summary>
public sealed class DisabledEditTarget : IEditTarget
{
    public bool HostMediatedClipboard => false;

    public bool HasPlatformEditing => false;

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

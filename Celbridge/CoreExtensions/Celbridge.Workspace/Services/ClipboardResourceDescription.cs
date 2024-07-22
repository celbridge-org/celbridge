using Celbridge.Clipboard;
using Celbridge.Projects;

namespace Celbridge.Workspace.Services;

public class ClipboardResourceDescription : IClipboardResourceContent
{
    public CopyResourceOperation Operation { get; set; }
    public List<ClipboardResourceItem> ResourceItems { get; } = new();
}

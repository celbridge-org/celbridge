using Celbridge.BaseLibrary.Clipboard;
using Celbridge.BaseLibrary.Project;

namespace Celbridge.Workspace.Services;

public class ClipboardResourceDescription : IClipboardResourceContent
{
    public CopyResourceOperation Operation { get; set; }
    public List<ClipboardResourceItem> ResourceItems { get; } = new();
}

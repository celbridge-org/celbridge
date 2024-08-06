using Celbridge.Clipboard;
using Celbridge.Projects;

namespace Celbridge.Workspace.Services;

public class ResourceTransfer : IResourceTransfer
{
    public ResourceTransferMode TransferMode { get; set; }
    public List<ResourceTransferItem> TransferItems { get; } = new();
}

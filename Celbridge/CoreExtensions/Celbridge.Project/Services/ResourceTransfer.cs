using Celbridge.Resources;

namespace Celbridge.Project.Services;

public class ResourceTransfer : IResourceTransfer
{
    public ResourceTransferMode TransferMode { get; set; }
    public List<ResourceTransferItem> TransferItems { get; set;  } = new();
}

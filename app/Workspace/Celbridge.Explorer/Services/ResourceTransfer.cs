using Celbridge.DataTransfer;

namespace Celbridge.Explorer.Services;

public class ResourceTransfer : IResourceTransfer
{
    public DataTransferMode TransferMode { get; set; }
    public List<ResourceTransferItem> TransferItems { get; set;  } = new();
}

namespace Celbridge.Explorer.Helpers;

/// <summary>
/// Decides where resources dragged within the Explorer tree can land. The drop target is always a folder, since a
/// file row stands for the folder that contains it.
/// </summary>
internal static class ResourceDropRules
{
    /// <summary>
    /// Returns true if the dragged resources can land in the destination folder. The folder must accept additions,
    /// and it must lie outside every dragged folder. Each dragged resource from another folder must be a permitted
    /// resource in the destination. The check applies the transfer rule and asks the operation service the
    /// questions the move will ask, so the no-drop cursor agrees with the outcome.
    /// </summary>
    public static bool CanDropInto(
        IResourceOperationService operationService,
        ResourceKey destFolder,
        IReadOnlyList<ResourceItem> draggedResources)
    {
        if (operationService.CanAddToFolder(destFolder).IsFailure)
        {
            return false;
        }

        foreach (var resourceItem in draggedResources)
        {
            var sourceKey = resourceItem.Resource;
            var destKey = destFolder.Combine(sourceKey.ResourceName);

            if (TransferRules.CheckDestination(sourceKey, destKey).IsFailure)
            {
                return false;
            }

            // A move back into the resource's current folder changes nothing, so the policy is left out.
            if (sourceKey.GetParent() == destFolder)
            {
                continue;
            }

            if (operationService.CanCreateResource(destKey, resourceItem.IsFolder).IsFailure)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the dragged resources that a drop into the destination folder transfers, leaving out each resource
    /// already in the folder.
    /// </summary>
    public static List<ResourceKey> GetResourcesToTransfer(IReadOnlyList<ResourceItem> draggedResources, ResourceKey destFolder)
    {
        var sourceResources = new List<ResourceKey>();

        foreach (var resourceItem in draggedResources)
        {
            var sourceResource = resourceItem.Resource;
            if (sourceResource.GetParent() == destFolder)
            {
                continue;
            }

            sourceResources.Add(sourceResource);
        }

        return sourceResources;
    }
}

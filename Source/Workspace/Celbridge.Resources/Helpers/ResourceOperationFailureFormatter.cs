using Celbridge.Localization;
using Celbridge.Projects;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// Rewrites the failures of a resource operation for the surfaces a person reads, turning a typed
/// reason into a localized sentence. A failure carrying no typed reason keeps the message the
/// operation reported.
/// </summary>
public static class ResourceOperationFailureFormatter
{
    public static IReadOnlyList<FailedResource> Localize(
        IReadOnlyList<FailedResource> failedResources,
        ILocalizerService localizerService)
    {
        var localized = new List<FailedResource>(failedResources.Count);
        foreach (var failedResource in failedResources)
        {
            localized.Add(failedResource with
            {
                Message = FormatReason(failedResource, localizerService)
            });
        }

        return localized;
    }

    private static string FormatReason(FailedResource failedResource, ILocalizerService localizerService)
    {
        if (failedResource.Reason is ProjectFileMoveRefusedError refusedMove)
        {
            var stringKey = refusedMove.Refusal == ProjectFileMoveRefusal.OutsideProjectFolder
                ? "ProjectFile_CannotMoveOutOfProjectFolder"
                : "ProjectFile_CannotChangeExtension";

            return localizerService.GetString(stringKey, ProjectConstants.ProjectFileExtension);
        }

        return failedResource.Message;
    }
}

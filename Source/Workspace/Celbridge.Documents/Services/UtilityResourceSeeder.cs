using Celbridge.Logging;
using Celbridge.Packages;
using Celbridge.Workspace;

namespace Celbridge.Documents.Services;

/// <summary>
/// Seeds a utility's backing resource from its manifest template when the file is absent.
/// </summary>
public class UtilityResourceSeeder
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILogger<UtilityResourceSeeder> _logger;

    public UtilityResourceSeeder(
        IWorkspaceWrapper workspaceWrapper,
        ILogger<UtilityResourceSeeder> logger)
    {
        _workspaceWrapper = workspaceWrapper;
        _logger = logger;
    }

    /// <summary>
    /// Seeds the backing file for a utility contribution when it does not yet exist, resolving the resource from
    /// its descriptor. Returns Fail if the descriptor's resource is malformed or the seed write fails.
    /// </summary>
    public async Task<Result> SeedIfMissingAsync(EditorContribution contribution)
    {
        var descriptor = contribution.UtilityDescriptor;
        Guard.IsNotNull(descriptor);

        if (!ResourceKey.TryCreate(descriptor.Resource, out var resource))
        {
            return Result.Fail($"Utility declares an invalid resource: '{descriptor.Resource}'");
        }

        return await SeedFromContributionAsync(resource, contribution);
    }

    private async Task<Result> SeedFromContributionAsync(ResourceKey resource, EditorContribution contribution)
    {
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var infoResult = await resourceFileSystem.GetInfoAsync(resource);
        if (infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File)
        {
            return Result.Ok();
        }

        // A null return means a declared template file was missing or unreadable, and an empty array means
        // the utility declares no template. Both cases seed an empty file.
        var packageService = _workspaceWrapper.WorkspaceService.PackageService;
        var templateBytes = packageService.GetUtilityTemplateContent(contribution)
            ?? Array.Empty<byte>();

        var writeResult = await resourceFileSystem.WriteAllBytesAsync(resource, templateBytes);
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to seed utility backing file: '{resource}'")
                .WithErrors(writeResult);
        }

        _logger.LogTrace($"Seeded utility backing file: '{resource}'");

        return Result.Ok();
    }
}

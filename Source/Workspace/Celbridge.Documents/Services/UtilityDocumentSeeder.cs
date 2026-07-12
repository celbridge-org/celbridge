using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Services;

/// <summary>
/// Seeds a utility document's backing file from its manifest template when the file is absent.
/// Runs on the open path (ahead of the file-existence gate) so the launch command, auto-open, and
/// session restore all recover a missing file, and so it doubles as the recovery path when the user
/// deletes the .celbridge state file. Non-utility resources pass through untouched.
/// </summary>
public class UtilityDocumentSeeder
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILogger<UtilityDocumentSeeder> _logger;

    public UtilityDocumentSeeder(
        IWorkspaceWrapper workspaceWrapper,
        ILogger<UtilityDocumentSeeder> logger)
    {
        _workspaceWrapper = workspaceWrapper;
        _logger = logger;
    }

    /// <summary>
    /// Seeds the backing file for a utility resource when it does not yet exist. Returns Ok for any
    /// non-utility resource, for an already-present file, and after a successful seed. Returns Fail only
    /// when the resource resolves to a utility but the seed write fails.
    /// </summary>
    public async Task<Result> SeedIfMissingAsync(ResourceKey resource)
    {
        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;

        var factoryResult = documentsService.DocumentEditorRegistry.GetFactory(resource);
        if (factoryResult.IsFailure
            || factoryResult.Value is not CustomDocumentViewFactory { IsUtility: true } utilityFactory)
        {
            return Result.Ok();
        }

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var infoResult = await resourceFileSystem.GetInfoAsync(resource);
        if (infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File)
        {
            return Result.Ok();
        }

        // A null return means a declared template file was missing or unreadable; seed an empty file so
        // the open still succeeds and the editor initialises to its default state. An empty array means
        // the utility declares no template, which is also seeded as an empty file.
        var packageService = _workspaceWrapper.WorkspaceService.PackageService;
        var templateBytes = packageService.GetUtilityTemplateContent(utilityFactory.Contribution)
            ?? Array.Empty<byte>();

        var writeResult = await resourceFileSystem.WriteAllBytesAsync(resource, templateBytes);
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to seed utility document backing file: '{resource}'")
                .WithErrors(writeResult);
        }

        _logger.LogTrace($"Seeded utility document backing file: '{resource}'");

        return Result.Ok();
    }
}

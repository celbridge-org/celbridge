using Celbridge.Validators;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Resources.Services;

public class ResourceNameValidatorFactory : IResourceNameValidatorFactory
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceNameValidatorFactory(
        IStringLocalizer stringLocalizer,
        IWorkspaceWrapper workspaceWrapper)
    {
        _stringLocalizer = stringLocalizer;
        _workspaceWrapper = workspaceWrapper;
    }

    public IValidator CreateFromListing(
        ResourceKey parentFolder,
        IReadOnlyList<FolderChild> parentFolderChildren,
        ResourceNameKind kind,
        string? currentName = null)
    {
        return new ResourceNameValidator(
            _stringLocalizer,
            _workspaceWrapper,
            parentFolder,
            parentFolderChildren,
            kind,
            currentName);
    }

    public async Task<Result<IValidator>> CreateAsync(
        ResourceKey parentFolder,
        ResourceNameKind kind,
        string? currentName = null)
    {
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var enumerateResult = await resourceFileSystem.EnumerateFolderAsync(parentFolder);
        if (enumerateResult.IsFailure)
        {
            return Result<IValidator>.Fail($"Failed to list the parent folder: '{parentFolder}'")
                .WithErrors(enumerateResult);
        }

        var validator = CreateFromListing(parentFolder, enumerateResult.Value, kind, currentName);

        return Result<IValidator>.Ok(validator);
    }
}

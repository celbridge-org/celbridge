using Celbridge.Resources.Helpers;
using Celbridge.Validators;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Resources.Services;

public class ResourceNameValidator : IResourceNameValidator
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private ResourceKey? _parentFolder;
    private IReadOnlyList<FolderChild> _parentFolderChildren = [];

    public List<string> ValidNames { get; } = new();

    public bool ValidateAsFolder { get; set; }

    public ResourceNameValidator(
        IStringLocalizer stringLocalizer,
        IWorkspaceWrapper workspaceWrapper)
    {
        _stringLocalizer = stringLocalizer;
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task<Result> SetParentFolderAsync(ResourceKey parentFolder)
    {
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var enumerateResult = await resourceFileSystem.EnumerateFolderAsync(parentFolder);
        if (enumerateResult.IsFailure)
        {
            return Result.Fail($"Failed to list the parent folder: '{parentFolder}'")
                .WithErrors(enumerateResult);
        }

        _parentFolder = parentFolder;
        _parentFolderChildren = enumerateResult.Value;

        return Result.Ok();
    }

    public ValidationResult Validate(string input)
    {
        bool isValid = true;

        var errorList = new List<string>();

        // Check for invalid characters
        var invalidCharacters = Path.GetInvalidFileNameChars();
        string errorCharacters = string.Empty;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (invalidCharacters.Contains(c) &&
                !errorCharacters.Contains(c))
            {
                errorCharacters += c;
            }
        }

        if (!string.IsNullOrEmpty(errorCharacters))
        {
            isValid = false;
            var errorText = _stringLocalizer.GetString($"Validation_NameContainsInvalidCharacters", errorCharacters);
            errorList.Add(errorText);
        }

        // .cel is reserved for project metadata sidecars; reject any file
        // name that lands inside the namespace. Folders are not gated — the
        // reservation is about the file extension.
        if (!ValidateAsFolder
            && _workspaceWrapper.IsWorkspaceLoaded
            && _workspaceWrapper.WorkspaceService.ResourceService.Sidecars.IsSidecarFileName(input))
        {
            isValid = false;
            var errorText = _stringLocalizer.GetString("Validation_CelExtensionReserved");
            errorList.Add(errorText);
        }

        // Check for naming conflict with other resources in the parent folder.
        // Use case-insensitive comparison since Windows file system is case-insensitive.
        // Any name listed in ValidNames is always accepted as valid.
        if (!ValidNames.Any(name => name.Equals(input, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var folderChild in _parentFolderChildren)
            {
                var itemName = folderChild.Resource.ResourceName;
                if (itemName.Equals(input, StringComparison.OrdinalIgnoreCase))
                {
                    var errorKey = folderChild.IsFolder
                        ? "Validation_FolderNameAlreadyExists"
                        : "Validation_FileNameAlreadyExists";
                    var errorText = _stringLocalizer.GetString(errorKey, itemName);
                    errorList.Add(errorText);
                    isValid = false;
                    break;
                }
            }
        }

        // Policy gate: reject a name the resource policy would refuse, a reserved
        // path or a read-only root, so the user corrects it inline instead of
        // confirming a create the operation layer will then deny. Skipped when the
        // name is already invalid or empty, and when no parent folder is set.
        if (isValid
            && _parentFolder is ResourceKey parentFolder
            && !string.IsNullOrWhiteSpace(input)
            && _workspaceWrapper.IsWorkspaceLoaded)
        {
            var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
            var destinationKey = parentFolder.Combine(input);

            var policyResult = resourceService.Operations.CanCreateResource(destinationKey, ValidateAsFolder);
            if (policyResult.IsFailure)
            {
                isValid = false;
                errorList.Add(PolicyDenialFormatter.FormatReason(policyResult, input, _stringLocalizer));
            }
        }

        return new ValidationResult(isValid, errorList);
    }
}

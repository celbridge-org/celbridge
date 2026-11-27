using Celbridge.Resources.Helpers;
using Celbridge.Validators;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Resources.Services;

/// <summary>
/// Validates a name for a resource in one folder. The validator compares the name with a listing of the folder made
/// when the validator was created. An item added to the folder after that is not seen.
/// </summary>
internal sealed class ResourceNameValidator : IValidator
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ResourceKey _parentFolder;
    private readonly IReadOnlyList<FolderChild> _parentFolderChildren;
    private readonly ResourceNameKind _kind;
    private readonly string? _currentName;

    public ResourceNameValidator(
        IStringLocalizer stringLocalizer,
        IWorkspaceWrapper workspaceWrapper,
        ResourceKey parentFolder,
        IReadOnlyList<FolderChild> parentFolderChildren,
        ResourceNameKind kind,
        string? currentName)
    {
        _stringLocalizer = stringLocalizer;
        _workspaceWrapper = workspaceWrapper;
        _parentFolder = parentFolder;
        _parentFolderChildren = parentFolderChildren;
        _kind = kind;
        _currentName = currentName;
    }

    public ValidationResult Validate(string input)
    {
        bool isValid = true;

        var errorList = new List<string>();

        var isFolder = _kind == ResourceNameKind.Folder;

        if (_kind == ResourceNameKind.FileToSave
            && string.IsNullOrWhiteSpace(input))
        {
            isValid = false;
            var errorText = _stringLocalizer.GetString("Validation_FileNameRequired");
            errorList.Add(errorText);
        }

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

        // A file name may not end in .cel, which is reserved for the project's metadata sidecars. A folder name may end
        // in .cel.
        if (!isFolder
            && _workspaceWrapper.IsWorkspaceLoaded
            && _workspaceWrapper.WorkspaceService.ResourceService.Sidecars.IsSidecarFileName(input))
        {
            isValid = false;
            var errorText = _stringLocalizer.GetString("Validation_CelExtensionReserved");
            errorList.Add(errorText);
        }

        // Check for a clash with an item in the parent folder. The comparison ignores letter case, because the
        // Windows file system does. The current name never counts as taken, so a rename can keep the current name.
        var isCurrentName = _currentName is not null
            && _currentName.Equals(input, StringComparison.OrdinalIgnoreCase);
        if (!isCurrentName)
        {
            foreach (var folderChild in _parentFolderChildren)
            {
                var itemName = folderChild.Resource.ResourceName;
                if (itemName.Equals(input, StringComparison.OrdinalIgnoreCase))
                {
                    var conflictError = GetConflictError(folderChild);
                    if (conflictError is not null)
                    {
                        errorList.Add(conflictError);
                        isValid = false;
                    }
                    break;
                }
            }
        }

        // Ask the resource policy whether the item can be created. A reserved path or a read-only root then shows its
        // error while the user types.
        if (isValid
            && !string.IsNullOrWhiteSpace(input)
            && _workspaceWrapper.IsWorkspaceLoaded)
        {
            var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
            var destinationKey = _parentFolder.Combine(input);

            var policyResult = resourceService.Operations.CanCreateResource(destinationKey, isFolder);
            if (policyResult.IsFailure)
            {
                isValid = false;
                errorList.Add(PolicyDenialFormatter.FormatReason(policyResult, input, _stringLocalizer));
            }
        }

        return new ValidationResult(isValid, errorList);
    }

    // Returns the error when an item in the parent folder already has the name. Returns null when a save may replace
    // that item.
    private string? GetConflictError(FolderChild folderChild)
    {
        var itemName = folderChild.Resource.ResourceName;

        if (folderChild.IsFolder)
        {
            return _stringLocalizer.GetString("Validation_FolderNameAlreadyExists", itemName);
        }

        if (_kind != ResourceNameKind.FileToSave)
        {
            return _stringLocalizer.GetString("Validation_FileNameAlreadyExists", itemName);
        }

        var isReadOnly = (folderChild.Attributes & FileSystemAttributes.ReadOnly) != 0;
        if (isReadOnly)
        {
            return _stringLocalizer.GetString("Validation_FileIsReadOnly", itemName);
        }

        return null;
    }
}

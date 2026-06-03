using Celbridge.Resources.Helpers;
using Celbridge.Validators;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Resources.Services;

public class ResourceNameValidator : IResourceNameValidator
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public IFolderResource? ParentFolder { get; set; }

    public List<string> ValidNames { get; } = new();

    public bool ValidateAsFolder { get; set; }

    public ResourceNameValidator(
        IStringLocalizer stringLocalizer,
        IWorkspaceWrapper workspaceWrapper)
    {
        _stringLocalizer = stringLocalizer;
        _workspaceWrapper = workspaceWrapper;
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

        // Check for naming conflict with other resources in the parent folder.
        // Use case-insensitive comparison since Windows file system is case-insensitive.
        // Any name listed in ValidNames is always accepted as valid.
        if (!ValidNames.Any(name => name.Equals(input, StringComparison.OrdinalIgnoreCase)) &&
            ParentFolder is not null)
        {
            foreach (var childResource in ParentFolder.Children)
            {
                if (childResource.Name.Equals(input, StringComparison.OrdinalIgnoreCase))
                {
                    if (childResource is IFileResource)
                    {
                        var errorText = _stringLocalizer.GetString($"Validation_FileNameAlreadyExists", childResource.Name);
                        errorList.Add(errorText);
                    }
                    else if (childResource is IFolderResource)
                    {
                        var errorText = _stringLocalizer.GetString($"Validation_FolderNameAlreadyExists", childResource.Name);
                        errorList.Add(errorText);
                    }
                    isValid = false;
                    break;
                }
            }
        }

        // Policy gate: reject a name the resource policy would refuse — one the
        // ignore-file would immediately hide, a locked destination, or a
        // read-only root — so the user corrects it inline instead of confirming
        // a create the operation layer will then deny. Skipped when the name is
        // already invalid or empty, and when no workspace is loaded to resolve
        // the parent key.
        if (isValid
            && ParentFolder is not null
            && !string.IsNullOrWhiteSpace(input)
            && _workspaceWrapper.IsWorkspacePageLoaded)
        {
            var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
            var parentFolderKey = resourceService.Registry.GetResourceKey(ParentFolder);
            var destinationKey = parentFolderKey.Combine(input);

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

using Celbridge.Validators;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Services;

public class ResourceNameValidator : IResourceNameValidator
{
    private readonly IStringLocalizer _stringLocalizer;

    public IFolderResource? ParentFolder { get; set; }

    public List<string> ValidNames { get; } = new();

    public ResourceNameValidator(IStringLocalizer stringLocalizer)
    {
        _stringLocalizer = stringLocalizer;
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

        return new ValidationResult(isValid, errorList);
    }
}

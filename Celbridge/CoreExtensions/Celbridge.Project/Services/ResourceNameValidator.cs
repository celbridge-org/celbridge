using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Validators;
using Microsoft.Extensions.Localization;

namespace Celbridge.Project.Services;

public class ResourceNameValidator : IResourceNameValidator
{
    private readonly IStringLocalizer _stringLocalizer;

    public IFolderResource? ParentFolder { get; set; }

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

        // Check for naming conflict with other resources in the parent folder
        if (ParentFolder is not null)
        {
            foreach (var childResource in ParentFolder.Children)
            {
                if (childResource.Name == input)
                {
                    if (childResource is IFileResource)
                    {
                        var errorText = _stringLocalizer.GetString($"Validation_FileNameAlreadyExists", input);
                        errorList.Add(errorText);
                    }
                    else if (childResource is IFolderResource)
                    {
                        var errorText = _stringLocalizer.GetString($"Validation_FolderNameAlreadyExists", input);
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

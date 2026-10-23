using Celbridge.Validators;

namespace Celbridge.Resources;

/// <summary>
/// Applies a set of validation rules to an input string.
/// </summary>
public interface IResourceNameValidator : IValidator
{
    /// <summary>
    /// Any name listed in ValidNames is always accepted as valid, even if it has the same name
    /// as an existing resource in the parent folder.
    /// </summary>
    List<string> ValidNames { get; }

    /// <summary>
    /// Whether the proposed resource is a folder. Drives the folder-only pattern
    /// hint when the entered name is checked against the resource policy.
    /// </summary>
    bool ValidateAsFolder { get; set; }

    /// <summary>
    /// Lists the folder that will contain the proposed resource. The proposed name is then checked against the
    /// names of the items in the folder. Fails when the folder cannot be listed.
    /// </summary>
    Task<Result> SetParentFolderAsync(ResourceKey parentFolder);
}

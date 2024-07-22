using Celbridge.Resources;

namespace Celbridge.Validators;

/// <summary>
/// Applies a set of validation rules to an input string.
/// </summary>
public interface IResourceNameValidator : IValidator
{
    /// <summary>
    /// Parent folder resource that will contain the proposed resource.
    /// Used to check for conflicts with existing resource names.
    /// </summary>
    IFolderResource? ParentFolder { get; set; }

    /// <summary>
    /// Any name listed in ValidNames is always accepted as valid, even if it has the same name 
    /// as an existing resource in the ParentFolder.
    /// </summary>
    List<string> ValidNames { get; }
}

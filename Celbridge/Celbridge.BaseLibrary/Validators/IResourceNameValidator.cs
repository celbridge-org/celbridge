using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Validators;

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
}

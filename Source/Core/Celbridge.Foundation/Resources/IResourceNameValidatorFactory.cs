using Celbridge.Validators;

namespace Celbridge.Resources;

/// <summary>
/// Lists the kinds of resource name. Each kind has its own rules.
/// </summary>
public enum ResourceNameKind
{
    /// <summary>
    /// A file to create, rename or duplicate. The validator refuses the name of an existing item in the folder, and a
    /// name with the sidecar extension.
    /// </summary>
    File,

    /// <summary>
    /// A folder to create, rename, duplicate or extract an archive into. The validator refuses the name of an existing
    /// item in the folder.
    /// </summary>
    Folder,

    /// <summary>
    /// A file to save, which may replace an existing file. The validator accepts the name of an existing file, unless
    /// the file is read-only. The validator refuses the name of an existing folder, an empty name and a name with the
    /// sidecar extension.
    /// </summary>
    FileToSave
}

/// <summary>
/// Creates validators for the name of a resource in a folder. A validator checks the name's characters, the items
/// already in the folder and the resource policy. A validator never changes after the factory creates it.
/// </summary>
public interface IResourceNameValidatorFactory
{
    /// <summary>
    /// Returns a validator for names in a folder. The validator checks names against the caller's listing of the
    /// folder. The current name, when given, never counts as taken, so a rename can keep the current name.
    /// </summary>
    IValidator CreateFromListing(
        ResourceKey parentFolder,
        IReadOnlyList<FolderChild> parentFolderChildren,
        ResourceNameKind kind,
        string? currentName = null);

    /// <summary>
    /// Lists the folder, then passes the listing to CreateFromListing. Returns the validator from that call. Fails
    /// when the folder cannot be listed.
    /// </summary>
    Task<Result<IValidator>> CreateAsync(
        ResourceKey parentFolder,
        ResourceNameKind kind,
        string? currentName = null);
}

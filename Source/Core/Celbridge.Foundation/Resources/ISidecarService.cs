namespace Celbridge.Resources;

/// <summary>
/// The parsed result of a sidecar file: the TOML field dictionary. A `.cel`
/// file is just TOML; the field set is the whole content.
/// </summary>
public sealed record SidecarContent(
    IReadOnlyDictionary<string, object> Fields);

/// <summary>
/// Outcome categories for ISidecarService.ReadAsync. Distinguishes
/// "no sidecar on disk", "sidecar exists but is broken", and a successful
/// parse.
/// </summary>
public enum SidecarReadOutcome
{
    /// <summary>
    /// The parent resource has no sidecar file on disk.
    /// </summary>
    NoSidecar,

    /// <summary>
    /// The sidecar exists on disk but its content fails to parse.
    /// </summary>
    Broken,

    /// <summary>
    /// The sidecar exists on disk and parses cleanly. Content is populated.
    /// </summary>
    Healthy,
}

/// <summary>
/// The result of reading and parsing a sidecar file via ISidecarService.
/// </summary>
public sealed record SidecarReadResult(
    SidecarReadOutcome Outcome,
    SidecarContent? Content,
    string? FailureMessage);

/// <summary>
/// Outcome categories for ISidecarService mutator methods.
/// </summary>
public enum SidecarWriteOutcome
{
    /// <summary>
    /// The input matched the existing state; nothing was written to disk and
    /// no watcher events fired.
    /// </summary>
    NoChange,

    /// <summary>
    /// An existing sidecar file was mutated in place. The file's registry
    /// classification does not change as a result of a content update.
    /// </summary>
    Updated,

    /// <summary>
    /// A new sidecar file was created on disk. The registry needs to learn
    /// about the new file so subsequent reads see it.
    /// </summary>
    Created,
}

/// <summary>
/// Workspace-scoped service for reading and editing .cel sidecar files.
/// Exposes typed operations over the TOML field set.
/// </summary>
public interface ISidecarService
{
    /// <summary>
    /// True when the resource key's path ends with the sidecar extension.
    /// </summary>
    bool IsSidecarKey(ResourceKey resource);

    /// <summary>
    /// True when the supplied filename ends with the sidecar extension.
    /// </summary>
    bool IsSidecarFileName(string fileName);

    /// <summary>
    /// Builds the sidecar resource key for the given parent. Fails when the
    /// parent is empty, or when the parent already ends with the sidecar
    /// extension (which would produce an invalid .cel.cel key).
    /// </summary>
    Result<ResourceKey> GetSidecarKey(ResourceKey parent);

    /// <summary>
    /// True when the value can be written through the structured field
    /// surface: scalars (string, numeric, bool, datetime) and lists of those.
    /// Nested objects and mixed lists are rejected.
    /// </summary>
    bool IsIndexableValue(object? value);

    /// <summary>
    /// Reads and parses the sidecar storage for the given resource. For a regular
    /// file the storage is the sibling .cel sidecar; for a .cel file passed
    /// directly the resource itself is the storage. Returns NoSidecar when the
    /// storage file does not exist, Broken when it exists but does not parse,
    /// and Healthy with parsed content otherwise.
    /// </summary>
    Task<Result<SidecarReadResult>> ReadAsync(ResourceKey resource);

    /// <summary>
    /// Sets a single field, creating the sidecar if it does not
    /// already exist. The value must pass IsIndexableValue (scalar or list of
    /// scalars); other shapes are rejected at the service boundary. Returns
    /// the outcome so callers can distinguish a freshly-created sidecar
    /// (registry needs to learn about the new file) from an in-place update.
    /// </summary>
    Task<Result<SidecarWriteOutcome>> SetFieldAsync(ResourceKey resource, string field, object value);

    /// <summary>
    /// Removes a single field. No-op when the field or the sidecar
    /// is absent; the sidecar file is not created just to record an absence.
    /// </summary>
    Task<Result<SidecarWriteOutcome>> RemoveFieldAsync(ResourceKey resource, string field);

    /// <summary>
    /// Appends a tag to the sidecar's tags list, creating the sidecar if it
    /// does not already exist. Idempotent: adding a tag that is already present
    /// neither changes the list nor rewrites the file.
    /// </summary>
    Task<Result<SidecarWriteOutcome>> AddTagAsync(ResourceKey resource, string tag);

    /// <summary>
    /// Removes a tag from the sidecar's tags list. Idempotent. Dropping the
    /// final tag removes the tags field entirely. No-op when the sidecar is
    /// absent.
    /// </summary>
    Task<Result<SidecarWriteOutcome>> RemoveTagAsync(ResourceKey resource, string tag);
}

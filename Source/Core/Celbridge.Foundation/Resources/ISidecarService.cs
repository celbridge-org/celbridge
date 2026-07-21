namespace Celbridge.Resources;

/// <summary>
/// File-format constants for the .cel sidecar.
/// </summary>
public static class SidecarFile
{
    /// <summary>
    /// The on-disk file extension carried by sidecar files.
    /// </summary>
    public const string Extension = ".cel";
}

/// <summary>
/// Well-known root-level field names in a .cel sidecar. The leading underscore
/// marks each entry as system metadata; the encoder emits these fields at the top
/// of the file in canonical order. Add new reserved names here.
/// </summary>
public static class SidecarFieldNames
{
    /// <summary>
    /// The user's per-file editor choice (last "Open with..." selection).
    /// </summary>
    public const string Editor = "_editor";

    /// <summary>
    /// The tag list. Agent-facing tools surface its values under the domain key "tags".
    /// </summary>
    public const string Tags = "_tags";
}

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

    /// <summary>
    /// The mutation emptied an existing sidecar, so the now-blank file was
    /// deleted from disk. The registry needs to drop the removed file.
    /// </summary>
    Deleted,
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
    /// Atomically writes a batch of fields, creating the sidecar if it does
    /// not already exist. Every value must pass IsIndexableValue. Read once,
    /// mutate in memory, write once: if any value is rejected the file stays
    /// untouched. Returns the outcome so callers can distinguish a
    /// freshly-created sidecar (registry needs to learn about the new file)
    /// from an in-place update.
    /// </summary>
    Task<Result<SidecarWriteOutcome>> SetFieldsAsync(ResourceKey resource, IReadOnlyDictionary<string, object> fields);

    /// <summary>
    /// Atomically removes a batch of fields. Missing names are silent no-ops;
    /// the sidecar file is not created just to record absences.
    /// </summary>
    Task<Result<SidecarWriteOutcome>> RemoveFieldsAsync(ResourceKey resource, IReadOnlyList<string> names);

    /// <summary>
    /// Atomically appends a batch of tags to the sidecar's tag list, creating
    /// the sidecar if missing. Idempotent: tags already present do not
    /// duplicate or rewrite the file.
    /// </summary>
    Task<Result<SidecarWriteOutcome>> AddTagsAsync(ResourceKey resource, IReadOnlyList<string> tags);

    /// <summary>
    /// Atomically removes a batch of tags from the sidecar's tag list.
    /// Idempotent. Removing the final tag drops the tag list entirely. No-op
    /// when the sidecar is absent.
    /// </summary>
    Task<Result<SidecarWriteOutcome>> RemoveTagsAsync(ResourceKey resource, IReadOnlyList<string> tags);
}

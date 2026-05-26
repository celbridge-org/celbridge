namespace Celbridge.Resources;

/// <summary>
/// A named content block inside a sidecar file. The Name follows the
/// block-naming regex (lowercase, dotted, hyphens). Content is the verbatim
/// text between this fence line and the next fence line or end-of-file.
/// </summary>
public sealed record SidecarBlock(string Name, string Content);

/// <summary>
/// The parsed result of a sidecar file: the frontmatter dictionary plus the
/// ordered list of named content blocks.
/// </summary>
public sealed record SidecarContent(
    IReadOnlyDictionary<string, object> Frontmatter,
    IReadOnlyList<SidecarBlock> Blocks);

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
/// Workspace-scoped service for reading and editing .cel sidecar files.
/// Exposes typed operations (set field, add tag, write block, etc.) over the
/// frontmatter and named-blocks model.
/// </summary>
public interface ISidecarService
{
    /// <summary>
    /// True when the resource key's path ends with the sidecar extension.
    /// </summary>
    bool IsSidecarKey(ResourceKey resource);

    /// <summary>
    /// Builds the sidecar resource key for the given parent. Fails when the
    /// parent is empty, or when the parent already ends with the sidecar
    /// extension (which would produce an invalid .cel.cel key).
    /// </summary>
    Result<ResourceKey> GetSidecarKey(ResourceKey parent);

    /// <summary>
    /// True when the candidate string matches the block-naming rules
    /// (lowercase letters, digits, hyphens, dotted segments).
    /// </summary>
    bool IsValidBlockName(string name);

    /// <summary>
    /// True when the value can be written through the structured frontmatter
    /// surface: scalars (string, numeric, bool, datetime) and lists of those.
    /// Nested objects and mixed lists are rejected.
    /// </summary>
    bool IsIndexableValue(object? value);

    /// <summary>
    /// Reads and parses the sidecar storage for the given resource. For a regular
    /// file the storage is the sibling .cel sidecar; for a standalone .cel file
    /// the resource itself is the storage. Returns NoSidecar when the storage
    /// file does not exist, Broken when it exists but does not parse, and Healthy
    /// with parsed content otherwise.
    /// </summary>
    Task<Result<SidecarReadResult>> ReadAsync(ResourceKey resource);

    /// <summary>
    /// Sets a single frontmatter field, creating the sidecar if it does not
    /// already exist. The value must pass IsIndexableValue (scalar or list of
    /// scalars); other shapes are rejected at the service boundary.
    /// </summary>
    Task<Result> SetFieldAsync(ResourceKey resource, string field, object value);

    /// <summary>
    /// Removes a single frontmatter field. No-op when the field or the sidecar
    /// is absent; the sidecar file is not created just to record an absence.
    /// </summary>
    Task<Result> RemoveFieldAsync(ResourceKey resource, string field);

    /// <summary>
    /// Appends a tag to the sidecar's tags list, creating the sidecar if it
    /// does not already exist. Idempotent: adding a tag that is already present
    /// neither changes the list nor rewrites the file.
    /// </summary>
    Task<Result> AddTagAsync(ResourceKey resource, string tag);

    /// <summary>
    /// Removes a tag from the sidecar's tags list. Idempotent. Dropping the
    /// final tag removes the tags field entirely. No-op when the sidecar is
    /// absent.
    /// </summary>
    Task<Result> RemoveTagAsync(ResourceKey resource, string tag);

    /// <summary>
    /// Creates or overwrites a named content block, creating the sidecar if it
    /// does not already exist. The block id must pass IsValidBlockName.
    /// </summary>
    Task<Result> WriteBlockAsync(ResourceKey resource, string blockId, string content);

    /// <summary>
    /// Removes a named content block. No-op when the block or the sidecar is
    /// absent.
    /// </summary>
    Task<Result> RemoveBlockAsync(ResourceKey resource, string blockId);
}

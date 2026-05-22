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
/// Workspace-scoped service for reading, mutating, and writing .cel sidecar
/// files plus the validation helpers that surround them. IO goes through
/// IResourceFileSystem so the chokepoint's atomic-write + retry behaviour
/// applies uniformly. The TOML + named-blocks parser lives in the workspace
/// implementation so Foundation does not carry the Tomlyn dependency.
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
    /// Reads and parses the sidecar for the parent resource. Returns the
    /// NoSidecar outcome when the file does not exist, Broken when it exists
    /// but does not parse, and Healthy with parsed content otherwise. Fails
    /// when the parent key itself is invalid (empty or sidecar-shaped).
    /// </summary>
    Task<Result<SidecarReadResult>> ReadAsync(ResourceKey parent);

    /// <summary>
    /// Applies the mutator to the parent resource's sidecar frontmatter. If the
    /// sidecar is missing and createIfMissing is true, the helper creates an
    /// empty sidecar; if createIfMissing is false, missing sidecars short-
    /// circuit as a successful no-op. The blocks list is preserved verbatim.
    /// </summary>
    Task<Result> MutateFrontmatterAsync(
        ResourceKey parent,
        Action<Dictionary<string, object>> mutate,
        bool createIfMissing = true);

    /// <summary>
    /// Applies the mutator to the parent resource's named blocks list. If the
    /// sidecar is missing and createIfMissing is true, an empty sidecar is
    /// created before the mutation runs.
    /// </summary>
    Task<Result> MutateBlocksAsync(
        ResourceKey parent,
        Action<List<SidecarBlock>> mutate,
        bool createIfMissing = true);
}

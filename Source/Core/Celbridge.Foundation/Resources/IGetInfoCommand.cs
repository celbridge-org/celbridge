using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// A single block descriptor returned by IGetInfoCommand. The byte size is
/// included so a caller can decide whether to fetch the block via
/// IReadBlockCommand before paying the cost.
/// </summary>
public partial record class SidecarBlockDescriptor(string Id, int Size);

/// <summary>
/// Result of IGetInfoCommand: the resource's full sidecar frontmatter inline,
/// the ordered list of block descriptors, and a flag indicating whether a
/// sidecar was found. HasSidecar distinguishes a parent that has no sidecar
/// (empty Fields/Blocks, HasSidecar=false) from a parent whose sidecar exists
/// but is genuinely empty (empty Fields/Blocks, HasSidecar=true). The command
/// fails when the sidecar exists but is broken.
/// </summary>
public record class GetInfoResult(
    IReadOnlyDictionary<string, object> Fields,
    IReadOnlyList<SidecarBlockDescriptor> Blocks,
    bool HasSidecar);

/// <summary>
/// Returns the parent resource's full sidecar frontmatter and the ordered
/// list of block descriptors. Produces an empty result for resources without
/// a sidecar; fails when the sidecar exists but is broken.
/// </summary>
public interface IGetInfoCommand : IExecutableCommand<GetInfoResult>
{
    /// <summary>
    /// Parent resource whose .cel sidecar will be summarised.
    /// </summary>
    ResourceKey Resource { get; set; }
}

using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Result of IGetInfoCommand: the resource's full sidecar field set inline
/// and a flag indicating whether a sidecar was found. HasSidecar distinguishes
/// a parent that has no sidecar (empty Fields, HasSidecar=false) from a parent
/// whose sidecar exists but is genuinely empty (empty Fields, HasSidecar=true).
/// The command fails when the sidecar exists but is broken.
/// </summary>
public record class GetInfoResult(
    IReadOnlyDictionary<string, object> Fields,
    bool HasSidecar);

/// <summary>
/// Returns the parent resource's full sidecar field set inline. Produces an
/// empty result for resources without a sidecar; fails when the sidecar exists
/// but is broken.
/// </summary>
public interface IGetInfoCommand : IExecutableCommand<GetInfoResult>
{
    /// <summary>
    /// Parent resource whose .cel sidecar will be summarised.
    /// </summary>
    ResourceKey Resource { get; set; }
}

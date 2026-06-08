using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Enumerates every paired-sidecar parent resource whose .cel tag list
/// contains the given tag value. Results are sorted by resource key.
/// </summary>
public interface IFindTagCommand : IExecutableCommand<IReadOnlyList<ResourceKey>>
{
    /// <summary>
    /// Tag value to search for. Match is ordinal-exact; the command returns
    /// the empty list when no sidecar carries the tag.
    /// </summary>
    string Tag { get; set; }
}

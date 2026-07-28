using Celbridge.Packages;

namespace Celbridge.WebHost;

/// <summary>
/// The inputs a channel provider needs to create a channel for one open editor view.
/// </summary>
public sealed record CustomEditorChannelContext(ResolvedEditor ResolvedEditor, ResourceKey FileResource);

/// <summary>
/// The abstraction a package registers in DI to give a custom editor type a channel. Several can be
/// registered, each backing a different editor type.
/// </summary>
public interface ICustomEditorChannelProvider
{
    /// <summary>
    /// True when this provider backs the given editor contribution with a channel.
    /// </summary>
    bool CanCreate(EditorContribution contribution);

    /// <summary>
    /// Creates the channel for one open editor view. Cheap and non-throwing: real failures are deferred to
    /// the channel's own RPC handlers so they surface in the editor rather than failing the editor open.
    /// </summary>
    ICustomEditorChannel Create(CustomEditorChannelContext context);
}

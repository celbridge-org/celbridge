using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Sets or clears the filesystem read-only attribute on a file or folder.
/// Useful for unlocking files imported from archives, source-control checkouts,
/// network shares, or DCC-tool exports that arrive read-only by default.
/// Idempotent: setting an already-writeable file to writeable is a no-op.
/// </summary>
public interface ISetWriteableCommand : IExecutableCommand
{
    /// <summary>
    /// The resource key of the file or folder whose attribute is being toggled.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// When true, clears the read-only attribute so writes are allowed.
    /// When false, sets the read-only attribute so writes are refused.
    /// </summary>
    bool Writeable { get; set; }
}

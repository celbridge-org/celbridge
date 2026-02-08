using Celbridge.Commands;

namespace Celbridge.Explorer;

/// <summary>
/// Copies a file path to the clipboard.
/// </summary>
public interface ICopyFilePathCommand : IExecutableCommand
{
    /// <summary>
    /// The resource key whose file path should be copied to the clipboard.
    /// </summary>
    ResourceKey ResourceKey { get; set; }
}


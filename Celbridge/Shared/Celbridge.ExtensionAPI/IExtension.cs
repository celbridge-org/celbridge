using Celbridge.Foundation;

namespace Celbridge.ExtensionAPI;

/// <summary>
/// Interface for a Celbridge extension.
/// </summary>
public interface IExtension
{
    /// <summary>
    /// Initializes the extension with the provided context.
    /// </summary>
    public Result Initialize(IExtensionContext context);
}

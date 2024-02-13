namespace Celbridge.BaseLibrary.Extensions;

/// <summary>
/// The extension system discovers classes that implement this interface at startup.
/// All Celbridge extensions must contain a class that implements this interface.
/// </summary>
public interface IExtension
{
    /// <summary>
    /// Configures the dependency injection framework to support the types provided by the extension.
    /// </summary>
    void ConfigureServices(IServiceConfiguration config);
}

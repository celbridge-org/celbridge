namespace Celbridge.BaseLibrary.ServiceLocator;

/// <summary>
/// Manages dependencies for the application.
/// </summary>
public interface IServiceLocator
{
    /// <summary>
    /// The main application first creates the Service Provider used for dependency injection. 
    /// This method is then called to configure the Service Provider with all dependencies 
    /// required by the application.
    /// </summary>
    void Initialize(IServiceProvider serviceProvider);

    /// <summary>
    /// Returns an instance of a dependency that was previously configured via Initialize().
    /// Dependencies can usually be acquired by contructor initialization. Were this isn't 
    /// possible, this method can be used to acquire the dependency directly.
    /// </summary>
    T GetRequiredService<T>() where T : notnull;
}

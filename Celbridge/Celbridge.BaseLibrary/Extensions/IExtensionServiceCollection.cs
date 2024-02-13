namespace Celbridge.BaseLibrary.Extensions;

/// <summary>
/// Extensions use this interface to configure the dependency injection framework without using the 
/// Microsoft.Extensions.DependencyInjection package. The methods here correspond to a subset of the
/// Microsoft.Extensions.DependencyInjection.IServiceCollection interface.
/// </summary>
public interface IExtensionServiceCollection
{
    void AddTransient<T>()
        where T : class;

    void AddTransient<I, T>()
        where I : class
        where T : class;

    void AddSingleton<T>()
        where T : class;

    void AddSingleton<I, T>()
        where I : class
        where T : class;
}

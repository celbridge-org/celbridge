namespace Celbridge.BaseLibrary.Dependencies;

public interface IServiceConfiguration
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

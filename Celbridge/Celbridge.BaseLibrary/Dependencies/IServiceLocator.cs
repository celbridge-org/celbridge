namespace Celbridge.BaseLibrary.ServiceLocator;

public interface IServiceLocator
{
    void Initialize(IServiceProvider serviceProvider);

    T GetRequiredService<T>() where T : notnull;
}

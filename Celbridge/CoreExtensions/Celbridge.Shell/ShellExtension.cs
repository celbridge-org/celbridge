using Celbridge.BaseLibrary.Extensions;
using Celbridge.Shell.ViewModels;
using Celbridge.Shell.Views;

namespace Celbridge.Shell;

public class ShellExtension : IExtension
{
    public void ConfigureServices(IServiceConfiguration config)
    {
        config.AddTransient<ShellView>();
        config.AddTransient<ShellViewModel>();
    }

    public void Initialize()
    {}
}

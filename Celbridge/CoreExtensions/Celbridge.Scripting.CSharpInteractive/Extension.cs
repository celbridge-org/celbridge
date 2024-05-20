using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Scripting;
using Celbridge.Scripting.CSharpInteractive;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.CSharpScripting;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {}

    public Result Initialize()
    {
        var scriptingService = ServiceLocator.ServiceProvider.GetRequiredService<IScriptingService>();

        var scriptContextFactory = new CSharpInteractiveContextFactory();
        var result = scriptingService.RegisterScriptContextFactory(scriptContextFactory);

        return result;
    }
}

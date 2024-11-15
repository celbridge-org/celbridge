using Celbridge.Modules;
using Celbridge.Entities.Services;
using Celbridge.Entities.Commands;

namespace Celbridge.Entities;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IModuleServiceCollection config)
    {
        //
        // Register services
        //
        config.AddTransient<IEntityService, EntityService>();
        config.AddTransient<EntitySchemaRegistry>();
        config.AddTransient<EntityPrototypeRegistry>();

        //
        // Register commands
        //
        config.AddTransient<IModifyEntityCommand, ModifyEntityCommand>();
    }
}

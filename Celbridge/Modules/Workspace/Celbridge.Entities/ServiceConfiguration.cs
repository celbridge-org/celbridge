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
        config.AddTransient<ComponentSchemaRegistry>();
        config.AddTransient<ComponentPrototypeRegistry>();
        config.AddTransient<EntityRegistry>();

        //
        // Register commands
        //
        config.AddTransient<ISetPropertyCommand, SetPropertyCommand>();
        config.AddTransient<IUndoEntityCommand, UndoEntityCommand>();
        config.AddTransient<IRedoEntityCommand, RedoEntityCommand>();
        config.AddTransient<IPrintPropertyCommand, PrintPropertyCommand>();
        config.AddTransient<IAddComponentCommand, AddComponentCommand>();
        config.AddTransient<IRemoveComponentCommand, RemoveComponentCommand>();
    }
}

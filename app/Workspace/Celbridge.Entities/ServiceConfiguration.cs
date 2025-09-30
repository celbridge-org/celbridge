using Celbridge.Entities.Services;
using Celbridge.Entities.Commands;
using Celbridge.Activities.Services;

namespace Celbridge.Entities;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddTransient<IEntityService, EntityService>();
        services.AddTransient<ComponentConfigRegistry>();
        services.AddTransient<ComponentProxyService>();
        services.AddTransient<EntityRegistry>();
        services.AddTransient<IEntityAnnotation, EntityAnnotation>();
        services.AddTransient<IComponentEditorHelper, ComponentEditorHelper>();
        services.AddTransient<IComponentSchemaReaderFactory, ComponentSchemaReaderFactory>();

        //
        // Register commands
        //

        services.AddTransient<ISetPropertyCommand, SetPropertyCommand>();
        services.AddTransient<IUndoEntityCommand, UndoEntityCommand>();
        services.AddTransient<IRedoEntityCommand, RedoEntityCommand>();
        services.AddTransient<IPrintPropertyCommand, PrintPropertyCommand>();
        services.AddTransient<IAddComponentCommand, AddComponentCommand>();
        services.AddTransient<IRemoveComponentCommand, RemoveComponentCommand>();
        services.AddTransient<ICopyComponentCommand, CopyComponentCommand>();
        services.AddTransient<IMoveComponentCommand, MoveComponentCommand>();
    }
}

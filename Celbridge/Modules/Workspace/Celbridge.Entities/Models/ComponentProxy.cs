using Celbridge.Workspace;

namespace Celbridge.Entities.Models;

public class ComponentProxy : IComponentProxy
{
    private IEntityService _entityService;

    public ResourceKey Resource { get; }

    public int ComponentIndex { get; }

    public ComponentSchema Schema { get; }

    public IComponentDescriptor Descriptor { get; }

    public ComponentProxy(IServiceProvider serviceProvider, ResourceKey resource, int componentIndex, ComponentSchema schema, IComponentDescriptor descriptor)
    {
        var workspaceWraper = serviceProvider.GetRequiredService<IWorkspaceWrapper>();
        _entityService = workspaceWraper.WorkspaceService.EntityService;

        Resource = resource;
        ComponentIndex = componentIndex;
        Schema = schema;
        Descriptor = descriptor;
    }

    // Annotation info

    public ComponentStatus Status { get; set; }

    public string Tooltip { get; set; } = string.Empty;

    public virtual string GetComponentDescription()
    {
        throw new NotImplementedException();
    }

    public virtual string GetComponentTooltip()
    {
        throw new NotImplementedException();
    }

    public virtual object GetComponentForm()
    {
        throw new NotImplementedException();
    }

    public string GetString(string propertyPath, string defaultValue = "")
    {
        return _entityService.GetString(Resource, ComponentIndex, propertyPath, defaultValue);
    }
}

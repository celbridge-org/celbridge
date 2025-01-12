using Celbridge.Entities;
using Celbridge.Inspector.Models;
using Celbridge.Inspector.Views;
using Celbridge.Workspace;

namespace Celbridge.Inspector.Services;

public class FieldFactory : IFieldFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public FieldFactory(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper)
    {
        _serviceProvider = serviceProvider;
        _workspaceWrapper = workspaceWrapper;
    }

    public Result<IField> CreatePropertyField(IComponentProxy component, string propertyName)
    {
        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        var schema = component.Schema;

        // Get the property info
        var propertyInfos = schema.Properties.Where(p => p.PropertyName == propertyName).ToList();
        if (propertyInfos.Count != 1)
        {
            return Result<IField>.Fail($"Failed to find property '{propertyName}' for component '{component.Key}'");
        }
        var propertyInfo = propertyInfos[0];

        var header = propertyInfo.PropertyName;
        var text = propertyInfo.PropertyType;

        // Todo: Handle other property types

        return CreateStringField(component, propertyName);
    }

    private Result<IField> CreateStringField(IComponentProxy component, string propertyName)
    {
        var element = new StringField();
        element.ViewModel.Initialize(component, propertyName);
        element.TabIndex = component.Key.ComponentIndex;

        var field = new Field(element);

        return Result<IField>.Ok(field);
    }
}

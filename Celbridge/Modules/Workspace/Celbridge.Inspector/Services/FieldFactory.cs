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

    public Result<IField> CreatePropertyField(ResourceKey resource, int componentIndex, string propertyName)
    {
        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        // Get the component type
        var getTypeResult = entityService.GetComponentType(resource, componentIndex);
        if (getTypeResult.IsFailure)
        {
            return Result<IField>.Fail($"Failed to get component type for entity '{resource}' at component index {componentIndex}")
                .WithErrors(getTypeResult);
        }
        var componentType = getTypeResult.Value;

        // Get the component schema
        var getSchemaResult = entityService.GetComponentSchema(componentType);
        if (getSchemaResult.IsFailure)
        {
            return Result<IField>.Fail($"Failed to get component schema for component type '{componentType}'")
                .WithErrors(getSchemaResult);
        }
        var schema = getSchemaResult.Value;

        // Get the property info
        var propertyInfos = schema.Properties.Where(p => p.PropertyName == propertyName).ToList();
        if (propertyInfos.Count != 1)
        {
            return Result<IField>.Fail($"Failed to find component property '{propertyName}' for entity '{resource}' at component index {componentIndex}");
        }
        var propertyInfo = propertyInfos[0];

        var header = propertyInfo.PropertyName;
        var text = propertyInfo.PropertyType;

        // Todo: Handle other property types

        return CreateStringField(resource, componentIndex, propertyName);
    }

    private Result<IField> CreateStringField(ResourceKey resource, int componentIndex, string propertyName)
    {
        var element = new StringField();
        element.ViewModel.Initialize(resource, componentIndex, propertyName);
        element.TabIndex = componentIndex;

        var field = new Field(element);

        return Result<IField>.Ok(field);
    }
}

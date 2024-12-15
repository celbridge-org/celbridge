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

        // Get the component type info
        var getInfoResult = entityService.GetComponentTypeInfo(resource, componentIndex);
        if (getInfoResult.IsFailure)
        {
            return Result<IField>.Fail($"Failed to get component type info for resource '{resource}' at component index '{componentIndex}'")
                .WithErrors(getInfoResult);
        }
        var componentTypeInfo = getInfoResult.Value;

        // Get the property type info
        var propertyTypeInfos = componentTypeInfo.Properties.Where(p => p.PropertyName == propertyName).ToList();
        if (propertyTypeInfos.Count != 1)
        {
            return Result<IField>.Fail($"Failed to find component property '{propertyName}' for resource '{resource}' at component index '{componentIndex}'");
        }
        var propertyTypeInfo = propertyTypeInfos[0];

        var header = propertyTypeInfo.PropertyName;
        var text = propertyTypeInfo.PropertyType;

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

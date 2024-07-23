using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;
using System.Reflection;

namespace Celbridge.Commands.Services;

public class CommandSerializerContractResolver : DefaultContractResolver
{
    private bool _filterCommandProperties;

    public CommandSerializerContractResolver(bool filterCommandProperties)
    {
        _filterCommandProperties = filterCommandProperties;
    }

    protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
    {
        // Get the list of properties
        var properties = base.CreateProperties(type, memberSerialization);

        // Order properties alphabetically
        return properties.OrderBy(p => p.PropertyName).ToList();
    }

    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
    {
        var property = base.CreateProperty(member, memberSerialization);

        if (_filterCommandProperties)
        {
            // Filter command properties to avoid serializing sensitive information
            bool shouldSerialize = true;
            var declaringType = member.DeclaringType;
            if (declaringType is not null &&
                declaringType.IsAssignableTo(typeof(IExecutableCommand)))
            {
                switch (property.PropertyName)
                {
                    case nameof(IExecutableCommand.CommandId):
                    case nameof(IExecutableCommand.UndoGroupId):
                    case nameof(IExecutableCommand.UndoStackName):
                    case nameof(IExecutableCommand.ExecutionSource):
                        shouldSerialize = true;
                        break;
                    default:
                        shouldSerialize = false;
                        break;
                }
            }
            if (!shouldSerialize)
            {
                property.ShouldSerialize = instance => false;
            }
        }

        return property;
    }
}

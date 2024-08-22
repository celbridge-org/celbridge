using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;
using System.Reflection;
using Celbridge.Commands;

namespace Celbridge.Logging.Services;

public class CommandSerializerContractResolver : DefaultContractResolver
{
    protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
    {
        // Get the list of properties
        return base.CreateProperties(type, memberSerialization);
    }

    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
    {
        var property = base.CreateProperty(member, memberSerialization);

        // Do not serialize the CommandBase properties for IExecutableCommand.
        // The ExecutedCommandJsonConverter appends these properties so that they appear after the main command
        // properties in the serialized string.
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
                    shouldSerialize = false;
                    break;
            }
        }
        if (!shouldSerialize)
        {
            property.ShouldSerialize = instance => false;
        }

        return property;
    }
}

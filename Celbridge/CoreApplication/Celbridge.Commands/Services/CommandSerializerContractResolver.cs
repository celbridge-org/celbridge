using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;
using System.Reflection;

namespace Celbridge.Commands.Services;

public class CommandSerializerContractResolver : DefaultContractResolver
{
    private readonly HashSet<string> _propertiesToIgnore;

    public CommandSerializerContractResolver(IEnumerable<string> propertiesToIgnore)
    {
        _propertiesToIgnore = new HashSet<string>(propertiesToIgnore);
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

        if (_propertiesToIgnore.Contains(property.PropertyName!))
        {
            property.ShouldSerialize = instance => false;
        }

        return property;
    }
}

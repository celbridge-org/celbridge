using System.Text.Json;

namespace Celbridge.Entities.Services;

public class ComponentSchemaReaderFactory : IComponentSchemaReaderFactory
{
    public IComponentSchemaReader Create(ComponentSchema schema)
    {
        return new ComponentSchemaReader(schema);
    }
}

public class ComponentSchemaReader : IComponentSchemaReader
{
    public ComponentSchema Schema { get; init; }

    public ComponentSchemaReader(ComponentSchema schema)
    {
        Schema = schema;
    }

    public bool HasTag(string tag) => Schema.Tags.Contains(tag);

    public Result<ComponentPropertyInfo> GetPropertyInfo(string propertyName)
    {
        var name = propertyName.TrimStart('/');
        foreach (var property in Schema.Properties)
        {
            if (property.PropertyName.Equals(name, StringComparison.Ordinal))
            {
                return Result<ComponentPropertyInfo>.Ok(property);
            }
        }

        return Result<ComponentPropertyInfo>.Fail();
    }

    public bool GetBooleanAttribute(string attributeName, string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return Schema.Attributes.TryGetValue(attributeName, out var attributeValue) && bool.TryParse(attributeValue, out var result) && result;
        }

        var getInfoResult = GetPropertyInfo(propertyName);
        if (getInfoResult.IsFailure)
        {
            return false;
        }
        var propertyInfo = getInfoResult.Value;

        if (propertyInfo.Attributes.TryGetValue(attributeName, out var propertyAttributeValue))
        {
            return bool.TryParse(propertyAttributeValue, out var result) && result;
        }

        return false;
    }

    public string GetStringAttribute(string attributeName, string propertyName)
    {
        var attributes = GetAttributes(propertyName);

        if (attributes is not null &&
            attributes.TryGetValue(attributeName, out var propertyAttributeValue))
        {
            return propertyAttributeValue;
        }

        return string.Empty;
    }

    public int GetIntAttribute(string attributeName, string propertyName)
    {
        var attributes = GetAttributes(propertyName);
        if (attributes is null)
        {
            return 0;
        }

        return attributes.TryGetValue(attributeName, out var propertyValue) && int.TryParse(propertyValue, out var propertyIntValue) ? propertyIntValue : 0;
    }

    public double GetDoubleAttribute(string attributeName, string propertyName)
    {
        var attributes = GetAttributes(propertyName);
        if (attributes is null)
        {
            return 0;
        }

        return attributes.TryGetValue(attributeName, out var propertyValue) && double.TryParse(propertyValue, out var propertyDoubleValue) ? propertyDoubleValue : 0;
    }

    public Result<T> GetObjectAttribute<T>(string attributeName, string propertyName) where T : notnull
    {
        var attributes = GetAttributes(propertyName);
        if (attributes is null)
        {
            return Result<T>.Fail();
        }

        if (attributes.TryGetValue(attributeName, out var value))
        {
            try
            {
                var obj = JsonSerializer.Deserialize<T>(value);
                if (obj is not null)
                {
                    return Result<T>.Ok(obj);
                }
            }
            catch (JsonException ex)
            {
                return Result<T>.Fail($"Failed to deserialize object attribute: '{attributeName}'")
                    .WithException(ex);
            }
        }

        return Result<T>.Fail($"Failed to deserialize object attribute: '{attributeName}'");
    }

    private IReadOnlyDictionary<string, string>? GetAttributes(string propertyPath)
    {
        if (string.IsNullOrEmpty(propertyPath))
        {
            return Schema.Attributes;
        }
        else
        {
            var getInfoResult = GetPropertyInfo(propertyPath);
            if (getInfoResult.IsFailure)
            {
                return null;
            }
            var propertyInfo = getInfoResult.Value;

            return propertyInfo.Attributes;
        }
    }
}

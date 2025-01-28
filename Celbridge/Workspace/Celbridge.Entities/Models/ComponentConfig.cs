using Celbridge.Entities.Services;
using Json.Schema;
using System.Text.Json;

namespace Celbridge.Entities.Models;

public class ComponentConfig
{
    public const string ComponentTypeConstKey = $"/properties/{EntityUtils.ComponentTypeKey}/const";
    public const string AttributesKey = "attributes";
    public const string PropertiesKey = "properties";
    public const string PrototypeKey = "prototype";
    public const string FormKey = "form";
    public const string TypeKey = "type";

    public string ComponentType { get; }
    public int ComponentVersion { get; }
    public ComponentSchema ComponentSchema { get; }
    public JsonElement Prototype { get; }
    public Type ComponentEditorType { get; }
    public JsonSchema JsonSchema { get; }

    public ComponentConfig(
        string componentType, 
        int componentVersion, 
        ComponentSchema componentSchema, 
        JsonElement prototype,
        JsonSchema jsonSchema,
        Type componentEditorType)
    {
        ComponentType = componentType;
        ComponentVersion = componentVersion;
        ComponentSchema = componentSchema;
        Prototype = prototype;
        ComponentEditorType = componentEditorType;
        JsonSchema = jsonSchema;
    }
}

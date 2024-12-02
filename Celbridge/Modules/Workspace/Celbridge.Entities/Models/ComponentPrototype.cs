using System.Text.Json.Nodes;

namespace Celbridge.Entities.Models;

public class ComponentPrototype
{
    public JsonObject JsonObject { get; private set; }
    public ComponentSchema ComponentSchema { get; }

    private ComponentPrototype(JsonObject jsonObject, ComponentSchema componentSchema)
    {
        JsonObject = jsonObject;
        ComponentSchema = componentSchema;
    }

    public static ComponentPrototype Create(JsonObject jsonObject, ComponentSchema componentSchema)
    {
        return new ComponentPrototype(jsonObject, componentSchema);
    }
}

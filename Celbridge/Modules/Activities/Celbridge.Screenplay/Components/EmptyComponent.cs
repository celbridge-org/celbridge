using Celbridge.Entities;

namespace Celbridge.Screenplay.Components;

public class EmptyComponent : IComponentDescriptor
{
    public string SchemaJson => """
    {
        "type": "object",
        "additionalProperties": false,

        "attributes": {
        "allowMultipleComponents": true
        },

        "properties": {
        "_componentType": {
            "type": "string",
            "const": "Empty#1"
        },
        "comment": {
            "type": "string"
        }
        },

        "required": [
        "_componentType",
        "comment"
        ],

        "prototype": {
        "comment": ""
        }
    }
    """;
}

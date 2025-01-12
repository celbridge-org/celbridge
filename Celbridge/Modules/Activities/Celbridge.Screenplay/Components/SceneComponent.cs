using Celbridge.Entities;

namespace Celbridge.Screenplay.Components;

public class SceneComponent : IComponentDescriptor
{
    public const string ComponentType = "Scene";
    public const string SceneTitle = "/sceneTitle";
    public const string SceneDescription = "/sceneDescription";

    public string SchemaJson => """
    {
      "type": "object",
      "additionalProperties": false,

      "attributes": {
        "tags": [ "Screenplay" ]
      },

      "properties": {
        "_componentType": {
          "type": "string",
          "const": "Scene#1"
        },
        "sceneTitle": {
          "type": "string"
        },
        "sceneDescription": {
          "type": "string"
        }
      },

      "required": [
        "_componentType",
        "sceneTitle",
        "sceneDescription"
      ],

      "prototype": {
        "sceneTitle": "",
        "sceneDescription": ""
      }
    }    
    """;
}

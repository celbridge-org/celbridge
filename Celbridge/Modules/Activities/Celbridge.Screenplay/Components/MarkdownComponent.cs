using Celbridge.Entities;

namespace Celbridge.Screenplay.Components;

public class MarkdownComponent : IComponentDescriptor
{
    public string SchemaJson => """
    {
      "type": "object",
      "additionalProperties": false,

      "attributes": {
        "tags": [ "PrimaryComponent", "Markdown" ],
        "activityName": "Markdown"
      },

      "properties": {
        "_componentType": {
          "type": "string",
          "const": "Markdown#1"
        },
        "editorMode": {
          "type": "string",
          "enum": [ "Editor", "Preview", "EditorAndPreview" ]
        }
      },

      "required": [
        "_componentType",
        "editorMode"
      ],

      "prototype": {
        "editorMode": "EditorAndPreview"
      }
    }
    """;    
}

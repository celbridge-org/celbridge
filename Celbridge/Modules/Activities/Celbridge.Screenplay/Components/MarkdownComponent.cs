using Celbridge.Entities;
using Celbridge.Forms;

namespace Celbridge.Screenplay.Components;

public class MarkdownComponent : IComponentDescriptor
{
    public string SchemaJson => """
    {
      "type": "object",
      "additionalProperties": false,

      "attributes": {
        "tags": [ "Markdown" ]
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

    public Result<IForm> CreateDetailForm(IComponentProxy component)
    {
        return Result<IForm>.Fail();
    }
}

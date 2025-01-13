using Celbridge.Entities;
using Celbridge.Forms;

namespace Celbridge.Screenplay.Components;

public class ScreenplayActivityComponent : IComponentDescriptor
{
    public const string ActivityName = "Screenplay";

    public string SchemaJson => """
    {
      "type": "object",
      "additionalProperties": false,

      "attributes": {
        "tags": [ "Screenplay" ],
        "isActivityComponent": true
      },

      "properties": {
        "_componentType": {
          "type": "string",
          "const": "ScreenplayActivity#1"
        }
      },

      "required": [
        "_componentType"
      ],

      "prototype": {}
    }
    """;

    public IForm? CreateDetailForm(IComponentProxy component)
    {
        return null;
    }
}

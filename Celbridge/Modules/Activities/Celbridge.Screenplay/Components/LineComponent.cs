using Celbridge.Entities;

namespace Celbridge.Screenplay.Components;

public class LineComponent : IComponentDescriptor
{
    public const string ComponentType = "Line";
    public const string Character = "/character";
    public const string SourceText = "/sourceText";

    public string SchemaJson => """
    {
      "type": "object",
      "additionalProperties": false,

      "attributes": {
        "tags": [ "Screenplay" ],
        "allowMultipleComponents": true
      },

      "properties": {
        "_componentType": {
          "type": "string",
          "const": "Line#1"
        },
        "dialogueKey": {
          "type": "string"
        },
        "character": {
          "type": "string"
        },
        "sourceText": {
          "type": "string"
        },
        "contextNotes": {
          "type": "string"
        },
        "linePriority": {
          "type": "string"
        },
        "speakingTo": {
          "type": "string"
        },
        "gameArea": {
          "type": "string"
        },
        "timeConstraint": {
          "type": "string"
        },
        "direction": {
          "type": "string"
        },
        "platform": {
          "type": "string"
        },
        "productionStatus": {
          "type": "string"
        }
      },

      "required": [
        "_componentType",
        "dialogueKey",
        "character",
        "sourceText",
        "contextNotes",
        "linePriority",
        "speakingTo",
        "gameArea",
        "timeConstraint",
        "direction",
        "platform",
        "productionStatus"
      ],

      "prototype": {
        "dialogueKey": "",
        "character": "",
        "sourceText": "",
        "contextNotes": "",
        "linePriority": "",
        "speakingTo": "",
        "gameArea": "",
        "timeConstraint": "",
        "direction": "",
        "platform": "",
        "productionStatus": ""
      }
    }
    """;
}

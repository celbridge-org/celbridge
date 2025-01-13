using Celbridge.Entities;
using Celbridge.Forms;

namespace Celbridge.Screenplay.Components;

public class EmptyComponent : IComponentDescriptor
{
    private readonly IServiceProvider _serviceProvider;

    public EmptyComponent(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

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

    public IForm CreateDetailForm(IComponentProxy component)
    {
        var form = _serviceProvider.GetRequiredService<IForm>();

        // Todo: Add textblock element to form
        var textBlock = _serviceProvider.GetRequiredService<ITextBlockElement>();
        textBlock.Text = "Hello, World!";

        return form;
    }
}

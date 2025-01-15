using Celbridge.Entities;
using Celbridge.Forms;

namespace Celbridge.Screenplay.Components;

public class EmptyComponent : IComponentDescriptor
{
    private readonly IFormFactory _formFactory;

    public EmptyComponent(IFormFactory formFactory)
    {
        _formFactory = formFactory;
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

    public Result<IForm> CreateDetailForm(IComponentProxy component)
    {
        //var comment = component.GetString("/comment");

        //var textBlockA = _formFactory.CreateTextBlock()
        //    .WithText(comment);

        //var textBlockB = _formFactory.CreateTextBlock()
        //    .WithText("1");

        //var textBlockC = _formFactory.CreateTextBlock()
        //    .WithText("2");

        //var childPanel = _formFactory.CreateStackPanel(FormOrientation.Horizontal)
        //    .AddChildren(textBlockB, textBlockC);

        //var formPanel = _formFactory.CreateStackPanel(FormOrientation.Vertical)
        //    .AddChildren(textBlockA, childPanel);

        //var form = _formFactory.CreateForm(formPanel);

        // return Result<IForm>.Ok(form);

        return Result<IForm>.Fail();
    }
}

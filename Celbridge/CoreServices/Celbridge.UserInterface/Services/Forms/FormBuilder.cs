using Celbridge.Entities;
using Celbridge.Forms;

namespace Celbridge.UserInterface.Services.Forms;

public class FormBuilder : IFormBuilder
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TextBlockBuilder _textBlockBuilder;

    public FormBuilder(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _textBlockBuilder = _serviceProvider.GetRequiredService<TextBlockBuilder>();
    }

    public Result<FormInstance> Build(IForm form, ComponentKey component)
    {
        // Todo: Verify that the component key is valid

        // Construct a famework panel based on the form description
        var fameworkPanel = BuildFrameworkPanel(form.Panel, component);
        if (fameworkPanel is null)
        {
            return Result<FormInstance>.Fail($"Failed to build framework panel for form panel");
        }

        var formInstance = new FormInstance(form, fameworkPanel);

        return Result<FormInstance>.Ok(formInstance);
    }

    private Panel? BuildFrameworkPanel(IFormPanel formPanel, ComponentKey component)
    {
        // Create the Framework panel
        Panel? fameworkPanel = CreateFrameworkPanel(formPanel, component);
        if (fameworkPanel is null)
        {
            return null;
        }

        foreach (var formChild in formPanel.Children)
        {
            switch (formChild)
            {
                case IFormPanel childFormPanel:
                    {
                        var childFrameworkPanel = BuildFrameworkPanel(childFormPanel, component);
                        if (childFrameworkPanel is not null)
                        {
                            fameworkPanel.Children.Add(childFrameworkPanel);
                        }

                        break;
                    }

                case IFormElement childFormElement:
                    {
                        var childFrameworkElement = CreateFrameworkElement(childFormElement, component);
                        if (childFrameworkElement is not null)
                        {
                            fameworkPanel.Children.Add(childFrameworkElement);
                        }

                        break;
                    }
            }
        }

        return fameworkPanel;
    }

    private Panel? CreateFrameworkPanel(IFormPanel formPanel, ComponentKey component)
    {
        if (formPanel is IStackPanelElement stackPanelElement)
        {
            var fameworkStackPanel = new StackPanel();
            if (stackPanelElement.Orientation == FormOrientation.Vertical)
            {
                fameworkStackPanel.Orientation = Orientation.Vertical;
            }
            else
            {
                fameworkStackPanel.Orientation = Orientation.Horizontal;
            }

            return fameworkStackPanel;
        }

        return null;
    }

    private UIElement? CreateFrameworkElement(IFormElement formElement, ComponentKey component)
    {
        if (formElement is ITextBlockElement formTextBlock)
        {
            return _textBlockBuilder.Build(formTextBlock, component);
        }

        return null;
    }
}

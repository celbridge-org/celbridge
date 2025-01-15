using Celbridge.Forms;

namespace Celbridge.UserInterface.Services.Forms;

public class FormBuilder : IFormBuilder
{
    public Result<FormInstance> Build(IForm form)
    {
        // Construct a famework panel based on the form description
        var fameworkPanel = BuildFrameworkPanel(form.Panel);
        if (fameworkPanel is null)
        {
            return Result<FormInstance>.Fail($"Failed to build framework panel for form panel");
        }

        var formInstance = new FormInstance(form, fameworkPanel);

        return Result<FormInstance>.Ok(formInstance);
    }

    private Panel? BuildFrameworkPanel(IFormPanel formPanel)
    {
        // Create the Framework panel
        Panel? fameworkPanel = CreateFrameworkPanel(formPanel);
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
                        var childFrameworkPanel = BuildFrameworkPanel(childFormPanel);
                        if (childFrameworkPanel is not null)
                        {
                            fameworkPanel.Children.Add(childFrameworkPanel);
                        }

                        break;
                    }

                case IFormElement childFormElement:
                    {
                        var childFrameworkElement = CreateFrameworkElement(childFormElement);
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

    private Panel? CreateFrameworkPanel(IFormPanel formPanel)
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

    private UIElement? CreateFrameworkElement(IFormElement formElement)
    {
        if (formElement is ITextBlockElement formTextBlock)
        {
            var fameworkTextBlock = new TextBlock();
            fameworkTextBlock.Text = formTextBlock.Text;
            return fameworkTextBlock;
        }

        return null;
    }
}

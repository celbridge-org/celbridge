using Celbridge.Entities;
using Celbridge.Forms;
using Celbridge.UserInterface.ViewModels.Forms;
using Microsoft.UI.Text;
using Windows.UI.Text;

namespace Celbridge.UserInterface.Services.Forms;

public class FormBuilder : IFormBuilder
{
    private readonly IServiceProvider _serviceProvider;

    public FormBuilder(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Result<FormInstance> Build(IForm form, ComponentKey component)
    {
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
            var frameworkTextBlock = new TextBlock();

            if (formTextBlock.Italic)
            {
                frameworkTextBlock.FontStyle = FontStyle.Italic;
            }

            if (formTextBlock.Bold)
            {
                frameworkTextBlock.FontWeight = FontWeights.Bold;
            }

            if (formTextBlock.TextBinding is not null)
            {
                var viewModel = _serviceProvider.GetRequiredService<TextBlockViewModel>();

                viewModel.Component = component;
                viewModel.TextBinding = formTextBlock.TextBinding;

                frameworkTextBlock.DataContext = viewModel;

                frameworkTextBlock.Loaded += (s, e) =>
                {
                    if (formTextBlock.TextBinding is not null)
                    {
                        var bindingMode = formTextBlock.TextBinding.BindingMode switch
                        {
                            PropertyBindingMode.OneWay => BindingMode.OneWay,
                            PropertyBindingMode.TwoWay => BindingMode.TwoWay,
                            _ => BindingMode.OneTime
                        };

                        // Bind the ViewModel property
                        var binding = new Binding()
                        {
                            Path = new PropertyPath("Text"),
                            Mode = bindingMode
                        };
                        frameworkTextBlock.SetBinding(TextBlock.TextProperty, binding);
                    }

                    viewModel.Bind();
                };

                frameworkTextBlock.Unloaded += (s, e) =>
                {
                    if (formTextBlock.TextBinding is not null)
                    {
                        // Bind the ViewModel property
                        frameworkTextBlock.ClearValue(TextBlock.TextProperty);
                    }

                    viewModel.Unbind();
                };
            }
            else
            {
                frameworkTextBlock.Text = formTextBlock.Text;
            }

            return frameworkTextBlock;
        }

        return null;
    }
}

using Celbridge.Entities;
using Celbridge.Forms;
using Celbridge.UserInterface.ViewModels.Forms;
using Microsoft.UI.Text;
using Windows.UI.Text;

namespace Celbridge.UserInterface.Services.Forms;

public class TextBlockBuilder
{
    private readonly IServiceProvider _serviceProvider;

    public TextBlockBuilder(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public UIElement Build(ITextBlockElement formTextBlock, ComponentKey component)
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
}

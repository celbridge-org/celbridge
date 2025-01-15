using Celbridge.Forms;

namespace Celbridge.UserInterface.Services.Forms;

public class FormFactory : IFormFactory
{
    private readonly IServiceProvider _serviceProvider;

    public FormFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IForm CreateForm(IFormPanel formPanel)
    {
        var form = _serviceProvider.GetRequiredService<IForm>();
        form.Panel = formPanel;
        return form;
    }

    public IStackPanelElement CreateStackPanel(FormOrientation orientation)
    {
        var formPanel = _serviceProvider.GetRequiredService<IStackPanelElement>();
        formPanel.Orientation = orientation;
        return formPanel;
    }

    public ITextBlockElement CreateTextBlock()
    {
        var formTextBlock = _serviceProvider.GetRequiredService<ITextBlockElement>();
        return formTextBlock;
    }
}

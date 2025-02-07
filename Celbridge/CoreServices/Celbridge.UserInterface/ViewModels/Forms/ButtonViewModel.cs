using Celbridge.Forms;

namespace Celbridge.UserInterface.ViewModels.Forms;

public class ButtonViewModel : IButtonViewModel
{
    private IFormDataProvider? _formDataProvider;
    private string _buttonId = string.Empty;

    public Result Initialize(IFormDataProvider formDataProvider, string buttonId)
    {
        _formDataProvider = formDataProvider;
        _buttonId = buttonId;

        return Result.Ok();
    }

    public void OnButtonClicked()
    {
        _formDataProvider?.OnButtonClicked(_buttonId);
    }

    public void OnViewUnloaded()
    {
        _formDataProvider = null;
    }
}

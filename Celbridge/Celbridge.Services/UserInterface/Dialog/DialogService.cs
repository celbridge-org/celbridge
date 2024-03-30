using Celbridge.BaseLibrary.UserInterface.Dialog;

namespace Celbridge.Services.UserInterface.Dialog;

public class DialogService : IDialogService
{
    private readonly IDialogFactory _dialogFactory;

    public DialogService(
        IDialogFactory dialogFactory)
    {
        _dialogFactory = dialogFactory;
    }

    public async Task ShowAlertAsync(string titleText, string messageText, string closeText)
    {
        var dialog = _dialogFactory.CreateAlertDialog(titleText, messageText, closeText);

        await dialog.ShowDialogAsync();
    }
}

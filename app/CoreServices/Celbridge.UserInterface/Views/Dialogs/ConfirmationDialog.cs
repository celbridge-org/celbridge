using Celbridge.Dialog;

namespace Celbridge.UserInterface.Views;

public sealed partial class ConfirmationDialog : ContentDialog, IConfirmationDialog
{
    private readonly IStringLocalizer _stringLocalizer;

    public ConfirmationDialogViewModel ViewModel { get; }

    public string TitleText
    {
        get => ViewModel.TitleText;
        set => ViewModel.TitleText = value;
    }

    public string MessageText 
    { 
        get => ViewModel.MessageText;
        set => ViewModel.MessageText = value; 
    }

    public string OkString => _stringLocalizer.GetString("DialogButton_Ok");
    public string CancelString => _stringLocalizer.GetString("DialogButton_Cancel");

    public ConfirmationDialog()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<ConfirmationDialogViewModel>();

        this.DataContext(ViewModel, (dialog, vm) => dialog
            .Title(x => x.Binding(() => ViewModel.TitleText).Mode(BindingMode.OneWay))
            .PrimaryButtonText(OkString)
            .SecondaryButtonText(CancelString)
            .Content(new Grid()
                .HorizontalAlignment(HorizontalAlignment.Left)
                .VerticalAlignment(VerticalAlignment.Center)
                .Children(
                    new TextBlock()
                        .Text(x => x.Binding(() => ViewModel.MessageText).Mode(BindingMode.OneWay))
                        .TextWrapping(TextWrapping.WrapWholeWords)
                    )
                )
            );
    }

    public async Task<bool> ShowDialogAsync()
    {
        var showResult = await ShowAsync();

        if (showResult == ContentDialogResult.Primary)
        {
            return true;
        }

        return false;
    }
}





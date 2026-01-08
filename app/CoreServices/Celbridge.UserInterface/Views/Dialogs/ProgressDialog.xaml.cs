using Celbridge.Dialog;

namespace Celbridge.UserInterface.Views;

public sealed partial class ProgressDialog : ContentDialog, IProgressDialog
{
    public ProgressDialogViewModel ViewModel { get; }

    private bool _isShowing;

    public string TitleText
    {
        get => ViewModel.TitleText;
        set => ViewModel.TitleText = value;
    }

    public ProgressDialog()
    {
        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<ProgressDialogViewModel>();

        this.InitializeComponent();
    }

    public void ShowDialog()
    {
        if (_isShowing)
        {
            return;
        }

        _isShowing = true;
        var _ = ShowAsync();
    }

    public void HideDialog()
    {
        if (!_isShowing)
        {
            return;
        }
        Hide();
        _isShowing = false;
    }
}

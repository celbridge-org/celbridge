namespace CelLegacy.Views;

public sealed partial class ProgressDialog : ContentDialog
{
    public ProgressDialogViewModel ViewModel { get; private set; }

    public ProgressDialog(Action? onCancel)
    {
        this.InitializeComponent();
        ViewModel = LegacyServiceProvider.Services!.GetRequiredService<ProgressDialogViewModel>();
        ViewModel.ContentDialog = this;
        
        if (onCancel != null)
        {
            ViewModel.OnCancel = onCancel;
            PrimaryButtonText = "Cancel";
        }
    }
}

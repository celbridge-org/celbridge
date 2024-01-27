namespace Celbridge.Legacy.Views;

public sealed partial class NewProjectDialog : ContentDialog
{
    public NewProjectViewModel ViewModel { get; set; }
    
    public NewProjectDialog()
    {
        this.InitializeComponent();
        ViewModel = LegacyServiceProvider.Services!.GetRequiredService<NewProjectViewModel>();
    }
}

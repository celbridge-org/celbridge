namespace CelLegacy.Views;

public sealed partial class LeftNavigationBar : UserControl
{
    public LeftNavigationBarViewModel ViewModel { get; set; }

    public LeftNavigationBar()
    {
        this.InitializeComponent();
        ViewModel = LegacyServiceProvider.Services!.GetRequiredService<LeftNavigationBarViewModel>();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Expanded")
        {
        }
    }
}

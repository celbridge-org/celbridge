namespace CelLegacy.Views;

public sealed partial class DetailPanel : UserControl
{
    public DetailViewModel ViewModel { get; }

    public DetailPanel()
    {
        this.InitializeComponent();
        ViewModel = LegacyServiceProvider.Services!.GetRequiredService<DetailViewModel>();
        ViewModel.ItemCollection = DetailPropertyListView.Items;
    }
}

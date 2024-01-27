namespace Celbridge.Legacy.Views;

public partial class PathPropertyView : UserControl, IPropertyView
{
    public PathPropertyViewModel ViewModel { get; }

    public PathPropertyView()
    {
        this.InitializeComponent();

        var services = LegacyServiceProvider.Services!;
        ViewModel = services.GetRequiredService<PathPropertyViewModel>();
    }

    public void SetProperty(Property property, string labelText)
    {
        ViewModel.SetProperty(property, labelText);
    }

    public int ItemIndex
    {
        get => ViewModel.ItemIndex;
        set => ViewModel.ItemIndex = value;
    }

    public Result CreateChildViews()
    {
        return new SuccessResult();
    }
}

namespace CelLegacy.Views;

public partial class NumberPropertyView : UserControl, IPropertyView
{
    public NumberPropertyViewModel ViewModel { get; }

    public NumberPropertyView()
    {
        this.InitializeComponent();

        var services = LegacyServiceProvider.Services!;
        ViewModel = services.GetRequiredService<NumberPropertyViewModel>();
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

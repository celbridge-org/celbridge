namespace CelLegacy.Views;

public partial class TextPropertyView : UserControl, IPropertyView
{
    public TextPropertyViewModel ViewModel { get; }

    public TextPropertyView()
    {
        this.InitializeComponent();

        var services = LegacyServiceProvider.Services!;
        ViewModel = services.GetRequiredService<TextPropertyViewModel>();
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

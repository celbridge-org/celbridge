
namespace Celbridge.Inspector.Views;

public sealed partial class InspectorItemView : UserControl
{
    public InspectorItemView()
    {
        this.InitializeComponent();
    }

    public void ClearInspectorElements()
    {
        InspectorStackPanel.Children.Clear();
    }

    public void SetInspectorElements(List<UIElement> elements)
    {
        ClearInspectorElements();
        foreach (UIElement element in elements)
        {
            InspectorStackPanel.Children.Add(element);
        }
    }

    public void ClearDetailElements()
    {
        SelectedItemDetail.Children.Clear();
    }

    public void SetDetailElements(List<UIElement> elements)
    {
        ClearDetailElements();
        foreach (UIElement element in elements)
        {
            SelectedItemDetail.Children.Add(element);
        }

        SelectedItemDetail.Visibility = Visibility.Visible;
        DetailSplitter.Visibility = Visibility.Visible;
    }
}

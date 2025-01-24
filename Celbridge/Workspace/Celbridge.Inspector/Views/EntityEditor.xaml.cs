using Celbridge.Inspector.ViewModels;

namespace Celbridge.Inspector.Views;

public sealed partial class EntityEditor : UserControl
{
    public EntityEditorViewModel ViewModel { get; set; }

    public EntityEditor()
    {
        this.InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<EntityEditorViewModel>();
        DataContext = ViewModel;

        Loaded += EntityEditor_Loaded;
        Unloaded += EntityEditor_Unloaded;
    }

    private void EntityEditor_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.InspectedComponentChanged += ViewModel_InspectedComponentChanged;
    }

    private void EntityEditor_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.InspectedComponentChanged -= ViewModel_InspectedComponentChanged;
    }

    private void ViewModel_InspectedComponentChanged()
    {
        DetailScrollViewer.ScrollToVerticalOffset(0);
    }

    public void ClearComponentListPanel()
    {
        ComponentListPanel.Children.Clear();
    }

    public void PopulateComponentsPanel(List<UIElement> elements)
    {
        ClearComponentListPanel();
        foreach (UIElement element in elements)
        {
            ComponentListPanel.Children.Add(element);
        }
    }
}

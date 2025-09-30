using Celbridge.Inspector.ViewModels;

namespace Celbridge.Inspector.Views;

public sealed partial class ComponentValueEditor : UserControl
{
    public ComponentValueEditorViewModel ViewModel { get; set; }

    public ComponentValueEditor()
    {
        this.InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<ComponentValueEditorViewModel>();

        DataContext = ViewModel;

        ViewModel.OnFormCreated += ViewModel_OnFormCreated;
    }

    private void ViewModel_OnFormCreated(List<UIElement> uiElements)
    {
        FormPanel.Children.Clear();
        foreach (var uiElement in uiElements)
        {
            FormPanel.Children.Add(uiElement);
        }
    }
}

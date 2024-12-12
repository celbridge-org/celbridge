using Celbridge.Inspector.ViewModels;

namespace Celbridge.Inspector.Views;

public sealed partial class ComponentValueEditor : UserControl
{
    public ComponentValueEditorViewModel ViewModel { get; set; }

    public ComponentValueEditor()
    {
        this.InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<ComponentValueEditorViewModel>();

        DataContext = ViewModel;
    }
}

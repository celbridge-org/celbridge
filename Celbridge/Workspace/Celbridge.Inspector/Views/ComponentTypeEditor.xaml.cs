using Celbridge.Inspector.ViewModels;

namespace Celbridge.Inspector.Views;

public sealed partial class ComponentTypeEditor : UserControl
{
    public ComponentTypeEditorViewModel ViewModel { get; private set; }

    public ComponentTypeEditor()
	{
		this.InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<ComponentTypeEditorViewModel>();

        DataContext = ViewModel;
    }

    private void ListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        var componentType = e.ClickedItem as string;
        ViewModel.ComponentTypeClickedCommand.Execute(componentType);
    }

    private void TextBlock_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var textBlock = sender as TextBlock;
        Guard.IsNotNull(textBlock);

        var componentType = textBlock.Text;
        
        ViewModel.ComponentTypeClickedCommand.Execute(componentType);
    }
}

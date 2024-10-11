using Celbridge.Documents.ViewModels;

namespace Celbridge.Documents.Views;

public sealed partial class EditorPreviewView : UserControl
{
    public EditorPreviewViewModel ViewModel { get; }

    public EditorPreviewView()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<EditorPreviewViewModel>();

        var textBlock = new TextBlock()
            .Margin(4)
            .Text(x => x.Binding(() => ViewModel.PreviewHTML)
                .Mode(BindingMode.OneWay));
            
        this.DataContext(ViewModel)
            .Content(textBlock);
    }
}

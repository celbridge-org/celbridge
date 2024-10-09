using Celbridge.Documents.ViewModels;
using Celbridge.Foundation;

namespace Celbridge.Documents.Views;

public sealed partial class EditorPreviewView : UserControl
{
    public EditorPreviewViewModel ViewModel { get; }

    public EditorPreviewView()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<EditorPreviewViewModel>();

        this.DataContext(ViewModel);
    }
}

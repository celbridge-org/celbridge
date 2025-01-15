using Celbridge.Entities;

namespace Celbridge.UserInterface.ViewModels.Forms;

public partial class TextBlockViewModel : ObservableObject
{
    [ObservableProperty]
    private string _displayText = string.Empty;

    public void SetBinding(ComponentKey componentKey, string propertyPath)
    {
        // Todo: Store the component key and property path
        // Listen for component changes and update the display text
        // Also get the property when initializing the binding
        // Log binding errors - display empty text in error state
    }
}

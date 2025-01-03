using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Inspector.Models;

/// <summary>
/// Model class for a component item.
/// </summary>
public partial class ComponentItem : ObservableObject
{
    /// <summary>
    /// Gets or sets the component type.
    /// </summary>
    [ObservableProperty]
    private string _componentType = string.Empty;

    [ObservableProperty]
    private string _componentDescription = string.Empty;

    public ComponentItem DeepClone()
    {
        return new ComponentItem
        {
            ComponentType = ComponentType,
            ComponentDescription = ComponentDescription
        };
    }
}

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// The current-project row shown at the top of the recent-projects switcher. MenuFlyoutItem has no content
/// property, so this adds an Actions slot that the row template presents beside the project name.
/// </summary>
public sealed class CurrentProjectMenuItem : MenuFlyoutItem
{
    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions),
        typeof(object),
        typeof(CurrentProjectMenuItem),
        new PropertyMetadata(null));

    /// <summary>
    /// Content shown immediately to the right of the project name.
    /// </summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}

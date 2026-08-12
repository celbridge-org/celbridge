namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A caption naming the group of menu items that follows it. It is disabled so that pointer and keyboard
/// navigation skip over it, and its template renders no disabled state.
/// </summary>
public sealed class MenuSectionHeaderItem : MenuFlyoutItem
{
    public MenuSectionHeaderItem()
    {
        IsEnabled = false;
    }
}

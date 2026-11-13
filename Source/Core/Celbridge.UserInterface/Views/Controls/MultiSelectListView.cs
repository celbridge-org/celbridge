using Celbridge.Platform;
using Windows.System;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A row of a MultiSelectListView. A press with the platform's command modifier adds the row to the list's
/// selection, or takes it out.
/// </summary>
public sealed class MultiSelectListViewItem : ListViewItem
{
    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (TryToggleSelection(e))
        {
            return;
        }

        base.OnPointerPressed(e);
    }

    // UNO-BUG: a ListView toggles a row only for a press with Control, so on macOS a press with Command replaces
    // the selection. The row toggles itself here. Skipping the base handler keeps the ListView from treating the
    // press as a click.
    private bool TryToggleSelection(PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || !EditKeyboard.HasCommandModifier(e.KeyModifiers))
        {
            return false;
        }

        // The ListView selects a range for a press with Shift, with or without the command modifier.
        if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift))
        {
            return false;
        }

        // The ListView itself toggles a row for a press with Control.
        var platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        if (platformInfo.CommandModifier == CommandModifierKey.Control)
        {
            return false;
        }

        if (ItemsControl.ItemsControlFromItemContainer(this) is not ListViewBase listView)
        {
            return false;
        }

        var item = listView.ItemFromContainer(this);
        var wasSelected = listView.SelectedItems.Remove(item);
        if (!wasSelected)
        {
            listView.SelectedItems.Add(item);
        }
        IsSelected = !wasSelected;

        e.Handled = true;
        return true;
    }
}

/// <summary>
/// A ListView for lists that select several rows. A press with the platform's command modifier, which is Command on
/// macOS and Control elsewhere, adds a row to the selection or takes it out.
/// </summary>
public sealed class MultiSelectListView : ListView
{
    protected override DependencyObject GetContainerForItemOverride()
    {
        var container = new MultiSelectListViewItem();

        // Uno can only mark its own rows as generated. When Uno recycles a row from this override, it clears the item
        // container style and sets the style again on reuse, which rebuilds the row's template. A row that has the
        // style from the start keeps its template.
        if (ItemContainerStyle is not null)
        {
            container.Style = ItemContainerStyle;
        }

        return container;
    }

    protected override bool IsItemItsOwnContainerOverride(object item)
    {
        return item is MultiSelectListViewItem;
    }
}

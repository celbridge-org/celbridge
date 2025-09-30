using System.Collections.ObjectModel;

namespace Celbridge.UserInterface.Views.Controls;

public sealed partial class DropDownTextBox : UserControl
{
    public DropDownTextBox()
    {
        this.InitializeComponent();
        InputBox.KeyDown += InputBox_KeyDown;
    }

    public TextBox InnerTextBox => InputBox;

    public DropDownButton InnerButton => DropdownButton;

    public ListView InnerListView => OptionList;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(ObservableCollection<string>), typeof(DropDownTextBox), new PropertyMetadata(null));

    public ObservableCollection<string> ItemsSource
    {
        get => (ObservableCollection<string>)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register(nameof(SelectedValue), typeof(string), typeof(DropDownTextBox), new PropertyMetadata(string.Empty));

    public string SelectedValue
    {
        get => (string)GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSuggestions();
    }

    private void DropdownButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateSuggestions();
        SuggestionFlyout().ShowAt(InputBox);
    }

    private void OptionList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string selected)
        {
            SelectedValue = selected;
            InputBox.Text = selected;
            SuggestionFlyout().Hide();
        }
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var items = OptionList.ItemsSource as List<string>;
        if (items == null || items.Count == 0)
            return;

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Enter:
                if (OptionList.SelectedItem is string selected)
                {
                    SelectedValue = selected;
                    InputBox.Text = selected;
                    SuggestionFlyout().Hide();
                    e.Handled = true;
                }
                break;

            case Windows.System.VirtualKey.Escape:
                SuggestionFlyout().Hide();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Down:
            case Windows.System.VirtualKey.Up:
                // Allow form navigation to handle these keys
                break;

            case Windows.System.VirtualKey.Menu:
                if (Windows.UI.Core.CoreWindow.GetForCurrentThread()
                    .GetKeyState(Windows.System.VirtualKey.Down)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
                {
                    SuggestionFlyout().ShowAt(InputBox);
                    e.Handled = true;
                }
                break;
        }
    }

    private void UpdateSuggestions()
    {
        if (ItemsSource == null)
            return;

        string query = InputBox.Text ?? string.Empty;

        var filtered = ItemsSource
            .Where(item => item.Contains(query, System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        OptionList.ItemsSource = filtered;
    }

    private Flyout SuggestionFlyout()
    {
        return (Flyout)DropdownButton.Flyout;
    }
}

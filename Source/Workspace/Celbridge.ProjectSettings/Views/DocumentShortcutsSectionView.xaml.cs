using Celbridge.ProjectSettings.ViewModels;
using Celbridge.UserInterface.Helpers;

namespace Celbridge.ProjectSettings.Views;

/// <summary>
/// The Shortcuts section of the Project Settings: the document shortcut buttons the Utility Rail offers,
/// each edited through its own card.
/// </summary>
public sealed partial class DocumentShortcutsSectionView : UserControl
{
    private DocumentShortcutsSectionViewModel? _viewModel;

    // Supplied by the panel that owns this section. Assigning it refreshes the bindings so the section
    // populates once the panel hands over its instance.
    public DocumentShortcutsSectionViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            ShortcutCards.ItemsSource = value?.Shortcuts;
            Bindings?.Update();
        }
    }

    public DocumentShortcutsSectionView()
    {
        InitializeComponent();

        ShortcutCards.AddRequested += ShortcutCards_AddRequested;
    }

    private void ShortcutCards_AddRequested(object? sender, EventArgs e)
    {
        // The section records the addition, so there is nothing for the card list to report back.
        ViewModel?.AddShortcut();
    }

    private void OpenShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        var openButton = (FrameworkElement)sender;
        if (openButton.DataContext is not DocumentShortcutViewModel shortcut)
        {
            return;
        }

        ViewModel?.OpenShortcut(shortcut);
    }

    private void CommitField_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        FocusNavigationHelper.CommitFieldOnEnter(sender, e);
    }
}

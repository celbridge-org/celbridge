using Celbridge.Search.ViewModels;
using Celbridge.Workspace;

namespace Celbridge.Search.Views;

public sealed partial class SearchPanel : UserControl, ISearchPanel
{
    private readonly IPanelFocusService _panelFocusService;

    public SearchPanelViewModel ViewModel { get; }

    public SearchPanel()
    {
        _panelFocusService = ServiceLocator.AcquireService<IPanelFocusService>();
        ViewModel = ServiceLocator.AcquireService<SearchPanelViewModel>();

        this.InitializeComponent();
    }

    private void UserControl_GotFocus(object sender, RoutedEventArgs e)
    {
        _panelFocusService.SetFocusedPanel(WorkspacePanel.Search);
    }

    private void UserControl_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _panelFocusService.SetFocusedPanel(WorkspacePanel.Search);
    }

    private void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            ViewModel.ClearSearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void FileHeader_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is SearchFileResultViewModel fileResult)
        {
            fileResult.ToggleExpandedCommand.Execute(null);
        }
    }

    private void MatchLine_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is SearchMatchLineViewModel matchLine)
        {
            matchLine.NavigateCommand.Execute(null);
        }
    }

    public void FocusSearchInput()
    {
        SearchTextBox.Focus(FocusState.Programmatic);
        SearchTextBox.SelectAll();
    }
}

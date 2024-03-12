using Celbridge.Workspace.ViewModels;
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Uno.Themes.Markup;

namespace Celbridge.Workspace.Views;

public sealed partial class WorkspacePage : Page
{
    public WorkspacePageViewModel ViewModel { get; }

    private Grid _leftPanel;
    private Grid _centerPanel;
    private Grid _bottomPanel;
    private Grid _statusPanel;
    private Grid _rightPanel;
    private Grid _layoutRoot;

    private ColumnDefinition _leftPanelColumn;
    private ColumnDefinition _rightPanelColumn;
    private RowDefinition _bottomPanelRow;

    public WorkspacePage()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<WorkspacePageViewModel>();

        _leftPanel = new Grid()
            .Grid(column:0, row:0, rowSpan:3)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background("Red");

        _centerPanel = new Grid()
            .Grid(column: 1, row: 0)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background("Green");

        _bottomPanel = new Grid()
            .Grid(column: 1, row: 1)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background("Cyan");

        _statusPanel = new Grid()
            .Grid(column: 1, row: 2)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background("Yellow");

        _rightPanel = new Grid()
            .Grid(column: 2, row:0, rowSpan:3)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background("Blue");

#if WINDOWS

        // GridSplitters are not working on Skia. Attempting to instantiate the control causes
        // an exception to be thrown.

        var leftSplitter = new GridSplitter()
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            ResizeDirection = GridSplitter.GridResizeDirection.Auto,
            ResizeBehavior = GridSplitter.GridResizeBehavior.BasedOnAlignment,
        }
        .Grid(column:0);


        var rightSplitter = new GridSplitter()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            ResizeDirection = GridSplitter.GridResizeDirection.Auto,
            ResizeBehavior = GridSplitter.GridResizeBehavior.BasedOnAlignment,
        }
        .Grid(column: 2);

        var bottomSplitter = new GridSplitter()
        {
            VerticalAlignment = VerticalAlignment.Top,
            ResizeDirection = GridSplitter.GridResizeDirection.Auto,
            ResizeBehavior = GridSplitter.GridResizeBehavior.BasedOnAlignment,
        }
        .Grid(column: 1, row: 1);
#endif

        _layoutRoot = new Grid()
            .ColumnDefinitions("200, *, 200")
            .RowDefinitions("*, 200, 28")
            .Children(_leftPanel, _centerPanel, _bottomPanel, _statusPanel, _rightPanel
#if WINDOWS
            , leftSplitter, rightSplitter, bottomSplitter
#endif
            );

        _leftPanelColumn = _layoutRoot.ColumnDefinitions[0];
        _leftPanelColumn.MinWidth = 100;

        _rightPanelColumn = _layoutRoot.ColumnDefinitions[2];
        _rightPanelColumn.MinWidth = 100;

        _bottomPanelRow = _layoutRoot.RowDefinitions[1];
        _bottomPanelRow.MinHeight = 100;

        this.DataContext(ViewModel, (page, vm) => page
            .Background(Theme.Brushes.Background.Default)
            .Content(_layoutRoot)
            );

        Loaded += WorkspacePage_Loaded;
        Unloaded += WorkspacePage_Unloaded;
    }

    private void WorkspacePage_Loaded(object sender, RoutedEventArgs e)
    {
        var leftPanelWidth = ViewModel.LeftPanelWidth;
        var rightPanelWidth = ViewModel.RightPanelWidth;
        var bottomPanelHeight = ViewModel.BottomPanelHeight;

        if (leftPanelWidth > 0)
        {
            _leftPanelColumn.Width = new GridLength(leftPanelWidth);
        }
        if (rightPanelWidth > 0)
        {
            _rightPanelColumn.Width = new GridLength(rightPanelWidth);
        }
        if (bottomPanelHeight > 0)
        {
            _bottomPanelRow.Height = new GridLength(bottomPanelHeight);
        }

        _leftPanel.SizeChanged += (s, e) => ViewModel.LeftPanelWidth = (float)e.NewSize.Width;
        _rightPanel.SizeChanged += (s, e) => ViewModel.RightPanelWidth = (float)e.NewSize.Width;
        _bottomPanel.SizeChanged += (s, e) => ViewModel.BottomPanelHeight = (float)e.NewSize.Height;
    }

    private void WorkspacePage_Unloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= WorkspacePage_Loaded;
        Unloaded -= WorkspacePage_Unloaded;
    }
}





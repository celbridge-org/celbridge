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

    public WorkspacePage()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<WorkspacePageViewModel>();
        DataContext = ViewModel;

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

        // GridSplitters are not supported on Skia yet. Attempting to instantiate the control causes
        // an exception to be thrown.

        var leftSplitter = new GridSplitter()
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            ResizeDirection = GridSplitter.GridResizeDirection.Auto,
            ResizeBehavior = GridSplitter.GridResizeBehavior.BasedOnAlignment,
        }
        .Grid(column: 0);

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
            .ColumnDefinitions("300, *, 300")
            .RowDefinitions("*, 300, 28")
            .Children(_leftPanel, _centerPanel, _bottomPanel, _statusPanel, _rightPanel
#if WINDOWS
            , leftSplitter, rightSplitter, bottomSplitter
#endif
            );

        _leftPanelColumn = _layoutRoot.ColumnDefinitions[0];
        _rightPanelColumn = _layoutRoot.ColumnDefinitions[2];

        this.DataContext<WorkspacePageViewModel>((page, vm) => page
            .Background(Theme.Brushes.Background.Default)
            .Content(_layoutRoot)
            );
    }
}





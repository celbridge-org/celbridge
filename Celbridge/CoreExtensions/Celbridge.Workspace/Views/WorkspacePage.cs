using Celbridge.Workspace.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
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

        _layoutRoot = new Grid()
            .ColumnDefinitions("300, *, 300")
            .RowDefinitions("*, 200, 28")
            .Children(_leftPanel, _centerPanel, _bottomPanel, _statusPanel, _rightPanel);

        _leftPanelColumn = _layoutRoot.ColumnDefinitions[0];
        _rightPanelColumn = _layoutRoot.ColumnDefinitions[2];

        this.DataContext<WorkspacePageViewModel>((page, vm) => page
            .Background(Theme.Brushes.Background.Default)
            .Content(_layoutRoot)
            );
    }
}





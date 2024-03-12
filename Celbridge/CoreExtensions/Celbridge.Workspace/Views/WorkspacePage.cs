using Celbridge.Workspace.ViewModels;
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Uno.Themes.Markup;

namespace Celbridge.Workspace.Views;

public sealed partial class WorkspacePage : Page
{
    public WorkspacePageViewModel ViewModel { get; }

    private Button _toggleLeftPanelButton;
    private Button _toggleRightPanelButton;
    private Button _toggleBottomPanelButton;

    private Grid _leftPanel;
    private Grid _centerPanel;
    private Grid _bottomPanel;
    private Grid _statusPanel;
    private Grid _rightPanel;
    private Grid _layoutRoot;

    private ColumnDefinition _leftPanelColumn;
    private ColumnDefinition _rightPanelColumn;
    private RowDefinition _bottomPanelRow;

#if WINDOWS
    private GridSplitter _leftSplitter;
    private GridSplitter _rightSplitter;
    private GridSplitter _bottomSplitter;
#endif

    public WorkspacePage()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<WorkspacePageViewModel>();

        _toggleLeftPanelButton = new Button()
            .HorizontalAlignment(HorizontalAlignment.Left)
            .VerticalAlignment(VerticalAlignment.Top)
            .Command(ViewModel.ToggleLeftPanelCommand);

        _toggleRightPanelButton = new Button()
            .HorizontalAlignment(HorizontalAlignment.Right)
            .VerticalAlignment(VerticalAlignment.Top)
            .Command(ViewModel.ToggleRightPanelCommand);

        _toggleBottomPanelButton = new Button()
            .HorizontalAlignment(HorizontalAlignment.Right)
            .VerticalAlignment(VerticalAlignment.Top)
            .Command(ViewModel.ToggleBottomPanelCommand);

        _leftPanel = new Grid()
            .Grid(column: 0, row: 0, rowSpan: 3)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background(Colors.Red);

        _centerPanel = new Grid()
            .Grid(column: 1, row: 0)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background(Colors.Green)
            .Children(_toggleLeftPanelButton, _toggleRightPanelButton);

        _bottomPanel = new Grid()
            .Grid(column: 1, row: 1)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background(Colors.Cyan);

        _statusPanel = new Grid()
            .Grid(column: 1, row: 2)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background(Colors.DarkGreen)
            .Children(_toggleBottomPanelButton);

        _rightPanel = new Grid()
            .Grid(column: 2, row: 0, rowSpan: 3)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background(Colors.Blue);

#if WINDOWS

        // GridSplitters are not working on Skia. Attempting to instantiate the control causes
        // an exception to be thrown.

        _leftSplitter = new GridSplitter()
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            ResizeDirection = GridSplitter.GridResizeDirection.Auto,
            ResizeBehavior = GridSplitter.GridResizeBehavior.BasedOnAlignment,
        }
        .Grid(column:0);


        _rightSplitter = new GridSplitter()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            ResizeDirection = GridSplitter.GridResizeDirection.Auto,
            ResizeBehavior = GridSplitter.GridResizeBehavior.BasedOnAlignment,
        }
        .Grid(column: 2);

        _bottomSplitter = new GridSplitter()
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
            , _leftSplitter, _rightSplitter, _bottomSplitter
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

        UpdateExpandedPanels();

        _leftPanel.SizeChanged += (s, e) => ViewModel.LeftPanelWidth = (float)e.NewSize.Width;
        _rightPanel.SizeChanged += (s, e) => ViewModel.RightPanelWidth = (float)e.NewSize.Width;
        _bottomPanel.SizeChanged += (s, e) => ViewModel.BottomPanelHeight = (float)e.NewSize.Height;

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void WorkspacePage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

        Loaded -= WorkspacePage_Loaded;
        Unloaded -= WorkspacePage_Unloaded;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.LeftPanelExpanded):
            case nameof(ViewModel.RightPanelExpanded):
            case nameof(ViewModel.BottomPanelExpanded):
                UpdateExpandedPanels();
                break;
        }
    }

    private void UpdateExpandedPanels()
    {
        const string LeftChevron = "\ue76b";
        const string RightChevron = "\ue76c";
        const string DownChevron = "\ue70d";
        const string UpChevron = "\ue70e";

        _toggleLeftPanelButton.Content = new FontIcon
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Glyph = ViewModel.LeftPanelExpanded ? LeftChevron : RightChevron,
        };

        _toggleRightPanelButton.Content = new FontIcon
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Glyph = ViewModel.RightPanelExpanded ? RightChevron : LeftChevron,
        };

        _toggleBottomPanelButton.Content = new FontIcon
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Glyph = ViewModel.BottomPanelExpanded ? DownChevron : UpChevron,
        };

        if (ViewModel.LeftPanelExpanded)
        {
#if WINDOWS
            _leftSplitter.Visibility = Visibility.Visible;
#endif
            _leftPanel.Visibility = Visibility.Visible;
            _leftPanelColumn.MinWidth = 100;
            _leftPanelColumn.Width = new GridLength(ViewModel.LeftPanelWidth);
        }
        else
        {
#if WINDOWS
            _leftSplitter.Visibility = Visibility.Collapsed;
#endif
            _leftPanel.Visibility = Visibility.Collapsed;
            _leftPanelColumn.MinWidth = 0;
            _leftPanelColumn.Width = new GridLength(0);
        }

        if (ViewModel.RightPanelExpanded)
        {
#if WINDOWS
            _rightSplitter.Visibility = Visibility.Visible;
#endif
            _rightPanel.Visibility = Visibility.Visible;
            _rightPanelColumn.MinWidth = 100;
            _rightPanelColumn.Width = new GridLength(ViewModel.RightPanelWidth);
        }
        else
        {
#if WINDOWS
            _rightSplitter.Visibility = Visibility.Collapsed;
#endif
            _rightPanel.Visibility = Visibility.Collapsed;
            _rightPanelColumn.MinWidth = 0;
            _rightPanelColumn.Width = new GridLength(0);
        }

        if (ViewModel.BottomPanelExpanded)
        {
#if WINDOWS
            _bottomSplitter.Visibility = Visibility.Visible;
#endif
            _bottomPanel.Visibility = Visibility.Visible;
            _bottomPanelRow.MinHeight = 100;
            _bottomPanelRow.Height = new GridLength(ViewModel.BottomPanelHeight);
        }
        else
        {
#if WINDOWS
            _bottomSplitter.Visibility = Visibility.Collapsed;
#endif
            _bottomPanel.Visibility = Visibility.Collapsed;
            _bottomPanelRow.MinHeight = 0;
            _bottomPanelRow.Height = new GridLength(0);
        }
    }
}

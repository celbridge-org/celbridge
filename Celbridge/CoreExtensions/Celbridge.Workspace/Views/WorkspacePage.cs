using Celbridge.Workspace.ViewModels;
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Uno.Themes.Markup;

namespace Celbridge.Workspace.Views;

public sealed partial class WorkspacePage : Page
{
    private readonly FontFamily IconFontFamily = new FontFamily("Segoe MDL2 Assets");

    private const string LeftChevronGlyph = "\ue76b";
    private const string RightChevronGlyph = "\ue76c";
    private const string DownChevronGlyph = "\ue70d";
    private const string UpChevronGlyph = "\ue70e";

    public WorkspacePageViewModel ViewModel { get; }

    private Button _showLeftPanelButton;
    private Button _hideLeftPanelButton;
    private Button _showRightPanelButton;
    private Button _hideRightPanelButton;
    private Button _showBottomPanelButton;
    private Button _hideBottomPanelButton;

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
    private GridSplitter _leftPanelSplitter;
    private GridSplitter _rightPanelSplitter;
    private GridSplitter _bottomPanelSplitter;
#endif

    public WorkspacePage()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<WorkspacePageViewModel>();

        //
        // Define panel visibility buttons
        //

        _showLeftPanelButton = new Button()
            .HorizontalAlignment(HorizontalAlignment.Left)
            .VerticalAlignment(VerticalAlignment.Top)
            .Command(ViewModel.ToggleLeftPanelCommand)
            .Content(new FontIcon
            {
                FontFamily = IconFontFamily,
                Glyph = RightChevronGlyph,
            });

        _hideLeftPanelButton = new Button()
            .HorizontalAlignment(HorizontalAlignment.Right)
            .VerticalAlignment(VerticalAlignment.Top)
            .Command(ViewModel.ToggleLeftPanelCommand)
            .Content(new FontIcon
            {
                FontFamily = IconFontFamily,
                Glyph = LeftChevronGlyph,
            });

        _showRightPanelButton = new Button()
            .HorizontalAlignment(HorizontalAlignment.Right)
            .VerticalAlignment(VerticalAlignment.Top)
            .Command(ViewModel.ToggleRightPanelCommand)
            .Content(new FontIcon
            {
                FontFamily = IconFontFamily,
                Glyph = LeftChevronGlyph,
            });

        _hideRightPanelButton = new Button()
            .HorizontalAlignment(HorizontalAlignment.Left)
            .VerticalAlignment(VerticalAlignment.Top)
            .Command(ViewModel.ToggleRightPanelCommand)
            .Content(new FontIcon
            {
                FontFamily = IconFontFamily,
                Glyph = RightChevronGlyph,
            });

        _showBottomPanelButton = new Button()
            .HorizontalAlignment(HorizontalAlignment.Right)
            .VerticalAlignment(VerticalAlignment.Top)
            .Command(ViewModel.ToggleBottomPanelCommand)
            .Content(new FontIcon
            {
                FontFamily = IconFontFamily,
                Glyph = UpChevronGlyph,
            });

        _hideBottomPanelButton = new Button()
            .HorizontalAlignment(HorizontalAlignment.Right)
            .VerticalAlignment(VerticalAlignment.Top)
            .Command(ViewModel.ToggleBottomPanelCommand)
            .Content(new FontIcon
            {
                FontFamily = IconFontFamily,
                Glyph = DownChevronGlyph,
            });

        //
        // Define workspace panels
        //

        _leftPanel = new Grid()
            .Grid(column: 0, row: 0, rowSpan: 3)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background(Colors.Red)
            .Children(_hideLeftPanelButton);

        _centerPanel = new Grid()
            .Grid(column: 1, row: 0)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background(Colors.Green)
            .Children(_showLeftPanelButton, _showRightPanelButton);

        _rightPanel = new Grid()
            .Grid(column: 2, row: 0, rowSpan: 3)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background(Colors.Blue)
            .Children(_hideRightPanelButton);

        _bottomPanel = new Grid()
            .Grid(column: 1, row: 1)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background(Colors.Cyan)
            .Children(_hideBottomPanelButton);

        _statusPanel = new Grid()
            .Grid(column: 1, row: 2)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background(Colors.DarkGreen)
            .Children(_showBottomPanelButton);

#if WINDOWS

        //
        // Define grid splitters
        // Note: GridSplitters are not working on Skia. Attempting to instantiate the control causes
        // an exception to be thrown. Only instantiate GridSplitters on Windows for now.
        //

        _leftPanelSplitter = new GridSplitter()
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            ResizeDirection = GridSplitter.GridResizeDirection.Auto,
            ResizeBehavior = GridSplitter.GridResizeBehavior.BasedOnAlignment,
        }
        .Grid(column:0);


        _rightPanelSplitter = new GridSplitter()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            ResizeDirection = GridSplitter.GridResizeDirection.Auto,
            ResizeBehavior = GridSplitter.GridResizeBehavior.BasedOnAlignment,
        }
        .Grid(column: 2);

        _bottomPanelSplitter = new GridSplitter()
        {
            VerticalAlignment = VerticalAlignment.Top,
            ResizeDirection = GridSplitter.GridResizeDirection.Auto,
            ResizeBehavior = GridSplitter.GridResizeBehavior.BasedOnAlignment,
        }
        .Grid(column: 1, row: 1);
#endif

        //
        // Define Layout Root
        //

        _layoutRoot = new Grid()
            .ColumnDefinitions("200, *, 200")
            .RowDefinitions("*, 200, 28")
            .Children(_leftPanel, _centerPanel, _bottomPanel, _statusPanel, _rightPanel
#if WINDOWS
            , _leftPanelSplitter, _rightPanelSplitter, _bottomPanelSplitter
#endif
            );

        //
        // Set min size for resizing the splitter panels
        //

        _leftPanelColumn = _layoutRoot.ColumnDefinitions[0];
        _leftPanelColumn.MinWidth = 100;

        _rightPanelColumn = _layoutRoot.ColumnDefinitions[2];
        _rightPanelColumn.MinWidth = 100;

        _bottomPanelRow = _layoutRoot.RowDefinitions[1];
        _bottomPanelRow.MinHeight = 100;

        //
        // Set the data context and page content
        // 

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

        UpdatePanels();

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
            case nameof(ViewModel.LeftPanelVisible):
            case nameof(ViewModel.RightPanelVisible):
            case nameof(ViewModel.BottomPanelVisible):
                UpdatePanels();
                break;
        }
    }

    private void UpdatePanels()
    {
        //
        // Update button visibility based on panel visibility state
        //

        _showLeftPanelButton.Visibility = ViewModel.LeftPanelVisible ? Visibility.Collapsed : Visibility.Visible;
        _hideLeftPanelButton.Visibility = ViewModel.LeftPanelVisible ? Visibility.Visible : Visibility.Collapsed;

        _showRightPanelButton.Visibility = ViewModel.RightPanelVisible ? Visibility.Collapsed : Visibility.Visible;
        _hideRightPanelButton.Visibility = ViewModel.RightPanelVisible ? Visibility.Visible : Visibility.Collapsed;

        _showBottomPanelButton.Visibility = ViewModel.BottomPanelVisible ? Visibility.Collapsed : Visibility.Visible;
        _hideBottomPanelButton.Visibility = ViewModel.BottomPanelVisible ? Visibility.Visible : Visibility.Collapsed;

        //
        // Update panel and splitter visibility based on the panel visibility state
        //

        if (ViewModel.LeftPanelVisible)
        {
#if WINDOWS
            _leftPanelSplitter.Visibility = Visibility.Visible;
#endif
            _leftPanel.Visibility = Visibility.Visible;
            _leftPanelColumn.MinWidth = 100;
            _leftPanelColumn.Width = new GridLength(ViewModel.LeftPanelWidth);
        }
        else
        {
#if WINDOWS
            _leftPanelSplitter.Visibility = Visibility.Collapsed;
#endif
            _leftPanel.Visibility = Visibility.Collapsed;
            _leftPanelColumn.MinWidth = 0;
            _leftPanelColumn.Width = new GridLength(0);
        }

        if (ViewModel.RightPanelVisible)
        {
#if WINDOWS
            _rightPanelSplitter.Visibility = Visibility.Visible;
#endif
            _rightPanel.Visibility = Visibility.Visible;
            _rightPanelColumn.MinWidth = 100;
            _rightPanelColumn.Width = new GridLength(ViewModel.RightPanelWidth);
        }
        else
        {
#if WINDOWS
            _rightPanelSplitter.Visibility = Visibility.Collapsed;
#endif
            _rightPanel.Visibility = Visibility.Collapsed;
            _rightPanelColumn.MinWidth = 0;
            _rightPanelColumn.Width = new GridLength(0);
        }

        if (ViewModel.BottomPanelVisible)
        {
#if WINDOWS
            _bottomPanelSplitter.Visibility = Visibility.Visible;
#endif
            _bottomPanel.Visibility = Visibility.Visible;
            _bottomPanelRow.MinHeight = 100;
            _bottomPanelRow.Height = new GridLength(ViewModel.BottomPanelHeight);
        }
        else
        {
#if WINDOWS
            _bottomPanelSplitter.Visibility = Visibility.Collapsed;
#endif
            _bottomPanel.Visibility = Visibility.Collapsed;
            _bottomPanelRow.MinHeight = 0;
            _bottomPanelRow.Height = new GridLength(0);
        }
    }
}

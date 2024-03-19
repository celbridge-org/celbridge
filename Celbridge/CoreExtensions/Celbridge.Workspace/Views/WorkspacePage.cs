using Celbridge.BaseLibrary.Workspace;
using Celbridge.Workspace.ViewModels;
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.DependencyInjection;

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
            .VerticalAlignment(VerticalAlignment.Center)
            .Margin(48, 0, 0, 0)
            .Command(ViewModel.ToggleLeftPanelCommand)
            .Content(new FontIcon
            {
                FontFamily = IconFontFamily,
                Glyph = RightChevronGlyph,
            });

        _hideLeftPanelButton = new Button()
            .HorizontalAlignment(HorizontalAlignment.Right)
            .VerticalAlignment(VerticalAlignment.Center)
            .Command(ViewModel.ToggleLeftPanelCommand)
            .Content(new FontIcon
            {
                FontFamily = IconFontFamily,
                Glyph = LeftChevronGlyph,
            });

        _showRightPanelButton = new Button()
            .HorizontalAlignment(HorizontalAlignment.Right)
            .VerticalAlignment(VerticalAlignment.Center)
            .Command(ViewModel.ToggleRightPanelCommand)
            .Content(new FontIcon
            {
                FontFamily = IconFontFamily,
                Glyph = LeftChevronGlyph,
            });

        _hideRightPanelButton = new Button()
            .HorizontalAlignment(HorizontalAlignment.Right)
            .VerticalAlignment(VerticalAlignment.Center)
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
            .VerticalAlignment(VerticalAlignment.Center)
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
            .RowDefinitions("40, *")
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background(ThemeResource.Get<Brush>("PanelBackgroundABrush"))
            .BorderBrush(ThemeResource.Get<Brush>("PanelBorderBrush"))
            .BorderThickness(new Thickness(1, 0, 1, 0))
            .Children(
                new Grid()
                    .Background(ThemeResource.Get<Brush>("PanelBackgroundABrush"))
                    .BorderBrush(ThemeResource.Get<Brush>("PanelBorderBrush"))
                    .BorderThickness(0, 0, 0, 1)
                    .Children(_hideLeftPanelButton));

        _centerPanel = new Grid()
            .Grid(column: 1, row: 0)
            .RowDefinitions("40, *")
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background(ThemeResource.Get<Brush>("ApplicationBackgroundBrush"))
            .Children(_showLeftPanelButton, _showRightPanelButton);

        _rightPanel = new Grid()
            .Grid(column: 2, row: 0, rowSpan: 3)
            .RowDefinitions("40, *")
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background(ThemeResource.Get<Brush>("PanelBackgroundABrush"))
            .BorderBrush(ThemeResource.Get<Brush>("PanelBorderBrush"))
            .BorderThickness(new Thickness(1, 0, 1, 0))
            .Children(
                new Grid()
                    .Background(ThemeResource.Get<Brush>("PanelBackgroundABrush"))
                    .BorderBrush(ThemeResource.Get<Brush>("PanelBorderBrush"))
                    .BorderThickness(0, 0, 0, 1)
                    .Children(_hideRightPanelButton));

        _bottomPanel = new Grid()
            .Grid(column: 1, row: 1)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background(ThemeResource.Get<Brush>("PanelBackgroundBBrush"))
            .BorderBrush(ThemeResource.Get<Brush>("PanelBorderBrush"))
            .BorderThickness(new Thickness(0, 1, 0, 0))
            .Children(
                new Grid()
                    .Background(ThemeResource.Get<Brush>("PanelBackgroundABrush"))
                    .BorderBrush(ThemeResource.Get<Brush>("PanelBorderBrush"))
                    .BorderThickness(0, 1, 0, 1)
                    .Height(40)
                    .VerticalAlignment(VerticalAlignment.Top)
                    .Children(_hideBottomPanelButton));

        _statusPanel = new Grid()
            .Grid(column: 1, row: 2)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Background(ThemeResource.Get<Brush>("PanelBackgroundABrush"))
            .BorderBrush(ThemeResource.Get<Brush>("PanelBorderBrush"))
            .BorderThickness(new Thickness(0, 1, 0, 0))
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
        .Grid(column:0, rowSpan:3)
        .Foreground(StaticResource.Get<Brush>("PanelBackgroundBBrush"));

        _rightPanelSplitter = new GridSplitter()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            ResizeDirection = GridSplitter.GridResizeDirection.Auto,
            ResizeBehavior = GridSplitter.GridResizeBehavior.BasedOnAlignment,
        }
        .Grid(column: 2, rowSpan: 3)
        .Foreground(StaticResource.Get<Brush>("PanelBackgroundBBrush"));

        _bottomPanelSplitter = new GridSplitter()
        {
            VerticalAlignment = VerticalAlignment.Top,
            ResizeDirection = GridSplitter.GridResizeDirection.Auto,
            ResizeBehavior = GridSplitter.GridResizeBehavior.BasedOnAlignment,
        }
        .Grid(column: 1, row: 1)
        .Foreground(StaticResource.Get<Brush>("PanelBackgroundBBrush"));
#endif

        //
        // Define Layout Root
        //

        _layoutRoot = new Grid()
            .ColumnDefinitions("200, *, 200")
            .RowDefinitions("*, 200, 32")
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
            .Content(_layoutRoot));

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
        ViewModel.WorkspacePanelsCreated += ViewModel_WorkspacePanelsCreated;

        ViewModel.OnWorkspacePageLoaded();
    }

    private void WorkspacePage_Unloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= WorkspacePage_Loaded;
        Unloaded -= WorkspacePage_Unloaded;

        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.WorkspacePanelsCreated -= ViewModel_WorkspacePanelsCreated;
        ViewModel.OnWorkspacePageUnloaded();
    }

    private void ViewModel_WorkspacePanelsCreated(Dictionary<WorkspacePanelType, UIElement> panels)
    {
        // Add the newly instantiated panels to the appropriate container element
        foreach (var (panelType, panel) in panels)
        {
            switch (panelType)
            {
                case WorkspacePanelType.ConsolePanel:
                    _bottomPanel.Children.Add(panel);
                    break;
                case WorkspacePanelType.StatusPanel:
                    _statusPanel.Children.Add(panel);
                    break;
                case WorkspacePanelType.ProjectPanel:
                    _leftPanel.Children.Add(panel);
                    break;
                case WorkspacePanelType.InspectorPanel:
                    _rightPanel.Children.Add(panel);
                    break;
                case WorkspacePanelType.DocumentsPanel:
                    // Insert the documents panel at the start of the children collection so that the left/right toggle buttons
                    // in the center panel take priority for accepting input.
                    _centerPanel.Children.Insert(0, panel);
                    break;
            }
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.IsLeftPanelVisible):
            case nameof(ViewModel.IsRightPanelVisible):
            case nameof(ViewModel.IsBottomPanelVisible):
                UpdatePanels();
                break;
        }
    }

    private void UpdatePanels()
    {
        //
        // Update button visibility based on panel visibility state
        //

        _showLeftPanelButton.Visibility = ViewModel.IsLeftPanelVisible ? Visibility.Collapsed : Visibility.Visible;
        _hideLeftPanelButton.Visibility = ViewModel.IsLeftPanelVisible ? Visibility.Visible : Visibility.Collapsed;

        _showRightPanelButton.Visibility = ViewModel.IsRightPanelVisible ? Visibility.Collapsed : Visibility.Visible;
        _hideRightPanelButton.Visibility = ViewModel.IsRightPanelVisible ? Visibility.Visible : Visibility.Collapsed;

        _showBottomPanelButton.Visibility = ViewModel.IsBottomPanelVisible ? Visibility.Collapsed : Visibility.Visible;
        _hideBottomPanelButton.Visibility = ViewModel.IsBottomPanelVisible ? Visibility.Visible : Visibility.Collapsed;

        //
        // Update panel and splitter visibility based on the panel visibility state
        //

        if (ViewModel.IsLeftPanelVisible)
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

        if (ViewModel.IsRightPanelVisible)
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

        if (ViewModel.IsBottomPanelVisible)
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

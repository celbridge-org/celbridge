using Celbridge.BaseLibrary.Settings;
using Celbridge.CommonUI.UserInterface;

namespace Celbridge.CommonUI.Views;

public sealed partial class WorkspaceView : Page
{
    private IEditorSettings _settings;
    private IUserInterfaceService _userInterfaceService;

    public WorkspaceViewModel ViewModel { get; set; }

    public WorkspaceView()
    {
        this.InitializeComponent();

        var serviceProvider = BaseLibrary.Core.Services.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<WorkspaceViewModel>();

        _settings = serviceProvider.GetRequiredService<IEditorSettings>();
        _userInterfaceService = serviceProvider.GetRequiredService<IUserInterfaceService>();

        ViewModel.WindowActivated += Window_Activated;

        Loaded += Page_Loaded;
    }

    private void Window_Activated(bool active)
    {
        /*
         // Todo: What does this then?
#if WINDOWS
        if (active)
        {
            var ActiveColor = ResourceUtils.Get<Windows.UI.Color>("TitleBarActiveColor");
            TitleBar.Background = new SolidColorBrush(ActiveColor);
        }
        else
        {
            var InactiveColor = ResourceUtils.Get<Windows.UI.Color>("TitleBarInactiveColor");
            TitleBar.Background = new SolidColorBrush(InactiveColor);
        }
#endif
        */
        UpdateSidePanels();
    }

    private void Page_Loaded(object? sender, RoutedEventArgs e)
    {
#if WINDOWS
        var mainWindow = _userInterfaceService.MainWindow;

        mainWindow.ExtendsContentIntoTitleBar = true;
        mainWindow.SetTitleBar(TitleBar);
#endif

        //BottomPanel.Children.Add(new ConsolePanel());
        //LeftPanel.Children.Add(new ProjectPanel());
        //LeftNavigationBar.Children.Add(new LeftNavigationBar());
        //StatusBar.Children.Add(new StatusBar());
        //CenterPanel.Children.Add(new DocumentsPanel());
        //RightPanel.Children.Add(new InspectorPanel());

        _settings.PropertyChanged += Settings_PropertyChanged;

        LeftSplitter.SizeChanged += LeftSplitter_SizeChanged;
        RightSplitter.SizeChanged += RightSplitter_SizeChanged;
        CenterPanelGrid.LayoutUpdated += CenterPanelGrid_LayoutUpdated;

        UpdateSidePanels();

        //var projectService = LegacyServiceProvider.Services!.GetRequiredService<IProjectService>();

        //async Task OpenPreviousProject()
        //{
        //    var openResult = await projectService.OpenPreviousProject();
        //    if (openResult is ErrorResult openError)
        //    {
        //        Log.Error(openError.Message);
        //    }
        //}

        //_ = OpenPreviousProject();
    }

    private void LeftSplitter_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _settings.LeftPanelWidth = (float)e.NewSize.Width;
    }

    private void RightSplitter_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _settings.RightPanelWidth = (float)e.NewSize.Width;
    }

    private void CenterPanelGrid_LayoutUpdated(object? sender, object e)
    {
        // For some reason, the panels get initialized first with a value of 4.
        // The code here only updates the value stored in settings when the width
        // is greater than the minWidth - using this as a hacky way to tell when we're
        // actually resizing using a GridSplitter.

        var leftWidth = (float)LeftPanelColumn.Width.Value;
        if (leftWidth > LeftPanelColumn.MinWidth)
        {
            _settings.LeftPanelWidth = leftWidth;
        }

        var rightWidth = (float)RightPanelColumn.Width.Value;
        if (rightWidth > RightPanelColumn.MinWidth)
        {
            _settings.RightPanelWidth = rightWidth;
        }

        var bottomHeight = (float)BottomPanelRow.Height.Value;
        if (bottomHeight > BottomPanelRow.MinHeight)
        {
            _settings.BottomPanelHeight = bottomHeight;
        }
    }

    private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "LeftPanelExpanded")
        {
            if (!_settings.LeftPanelExpanded)
            {
                // Record the current width before collapsing the panel
                _settings.LeftPanelWidth = (float)LeftPanelColumn.Width.Value;
            }
            UpdateSidePanels();
        }
        else if (e.PropertyName == "RightPanelExpanded")
        {
            if (!_settings.RightPanelExpanded)
            {
                // Record the current width before collapsing the panel
                _settings.RightPanelWidth = (float)RightPanelColumn.Width.Value;
            }
            UpdateSidePanels();
        }
        else if (e.PropertyName == "BottomPanelExpanded")
        {
            if (!_settings.BottomPanelExpanded)
            {
                // Record the current height before collapsing the panel
                _settings.BottomPanelHeight = (float)BottomPanelRow.Height.Value;
            }
            UpdateSidePanels();
        }
    }

    private void UpdateSidePanels()
    {
        // The trick here is to set the panel to collapsed _before_ setting the width to 0.
        // This avoids an exception in Skia.Gtk where it tries to perform layout on a 0 sized control.

        var leftPanelExpanded = _settings.LeftPanelExpanded;
        if (leftPanelExpanded)
        {
            LeftSplitter.Visibility = Visibility.Visible;
            LeftPanel.Visibility = Visibility.Visible;
            LeftPanelColumn.MinWidth = 100;
            LeftPanelColumn.Width = new GridLength(_settings.LeftPanelWidth);
        }
        else
        {
            LeftSplitter.Visibility = Visibility.Collapsed;
            LeftPanel.Visibility = Visibility.Collapsed;
            LeftPanelColumn.MinWidth = 0;
            LeftPanelColumn.Width = new GridLength(0);
        }

        var rightPanelExpanded = _settings.RightPanelExpanded;
        if (rightPanelExpanded)
        {
            RightSplitter.Visibility = Visibility.Visible;
            RightPanel.Visibility = Visibility.Visible;
            RightPanelColumn.MinWidth = 100;
            RightPanelColumn.Width = new GridLength(_settings.RightPanelWidth);
        }
        else
        {
            RightSplitter.Visibility = Visibility.Collapsed;
            RightPanel.Visibility = Visibility.Collapsed;
            RightPanelColumn.MinWidth = 0;
            RightPanelColumn.Width = new GridLength(0);
        }

        var bottomPanelExpanded = _settings.BottomPanelExpanded;
        if (bottomPanelExpanded)
        {
            BottomSplitter.Visibility = Visibility.Visible;
            BottomPanel.Visibility = Visibility.Visible;
            BottomPanelRow.MinHeight = 100;
            BottomPanelRow.Height = new GridLength(_settings.BottomPanelHeight);
        }
        else
        {
            BottomSplitter.Visibility = Visibility.Collapsed;
            BottomPanel.Visibility = Visibility.Collapsed;
            BottomPanelRow.MinHeight = 0;
            BottomPanelRow.Height = new GridLength(0);
        }
    }
}

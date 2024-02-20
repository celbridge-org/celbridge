namespace Celbridge.Legacy.Views;

public sealed partial class Shell : Page
{
    private ISettingsService? _settings;

    public ShellViewModel ViewModel { get; set; }

    public Shell()
    {
        this.InitializeComponent();

        ViewModel = LegacyServiceProvider.Services!.GetRequiredService<ShellViewModel>();

        ViewModel.WindowActivated += Window_Activated;

        Loaded += Page_Loaded;
    }

    private void Window_Activated(bool active)
    {
        UpdateSidePanels();
    }

    private void Page_Loaded(object? sender, RoutedEventArgs e)
    {
        BottomPanel.Children.Add(new ConsolePanel());
        LeftPanel.Children.Add(new ProjectPanel());
        LeftNavigationBar.Children.Add(new LeftNavigationBar());
        StatusBar.Children.Add(new StatusBar());
        CenterPanel.Children.Add(new DocumentsPanel());
        RightPanel.Children.Add(new InspectorPanel());

        _settings = LegacyServiceProvider.Services!.GetRequiredService<ISettingsService>();

        Guard.IsNotNull(_settings.EditorSettings);
        _settings.EditorSettings.PropertyChanged += Settings_PropertyChanged;

        LeftSplitter.SizeChanged += LeftSplitter_SizeChanged;
        RightSplitter.SizeChanged += RightSplitter_SizeChanged;
        CenterPanelGrid.LayoutUpdated += CenterPanelGrid_LayoutUpdated;

        UpdateSidePanels();

        var projectService = LegacyServiceProvider.Services!.GetRequiredService<IProjectService>();
        
        async Task OpenPreviousProject()
        {
            var openResult = await projectService.OpenPreviousProject();
            if (openResult is ErrorResult openError)
            {
                Log.Error(openError.Message);
            }
        }

        _ = OpenPreviousProject();
    }

    private void LeftSplitter_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        Guard.IsNotNull(_settings);
        Guard.IsNotNull(_settings.EditorSettings);
        _settings.EditorSettings.LeftPanelWidth = (float)e.NewSize.Width;
    }

    private void RightSplitter_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        Guard.IsNotNull(_settings);
        Guard.IsNotNull(_settings.EditorSettings);
        _settings.EditorSettings.RightPanelWidth = (float)e.NewSize.Width;
    }

    private void CenterPanelGrid_LayoutUpdated(object? sender, object e)
    {
        Guard.IsNotNull(_settings);
        Guard.IsNotNull(_settings.EditorSettings);

        // For some reason, the panels get initialized first with a value of 4.
        // The code here only updates the value stored in settings when the width
        // is greater than the minWidth - using this as a hacky way to tell when we're
        // actually resizing using a GridSplitter.

        var leftWidth = (float)LeftPanelColumn.Width.Value;
        if (leftWidth > LeftPanelColumn.MinWidth)
        {
            _settings.EditorSettings.LeftPanelWidth = leftWidth;
        }

        var rightWidth = (float)RightPanelColumn.Width.Value;
        if (rightWidth > RightPanelColumn.MinWidth)
        {
            _settings.EditorSettings.RightPanelWidth = rightWidth;
        }

        var bottomHeight = (float)BottomPanelRow.Height.Value;
        if (bottomHeight > BottomPanelRow.MinHeight)
        {
            _settings.EditorSettings.BottomPanelHeight = bottomHeight;
        }
    }

    private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Guard.IsNotNull(_settings);
        Guard.IsNotNull(_settings.EditorSettings);

        if (e.PropertyName == "LeftPanelExpanded")
        {
            if (!_settings.EditorSettings.LeftPanelExpanded)
            {
                // Record the current width before collapsing the panel
                _settings.EditorSettings.LeftPanelWidth = (float)LeftPanelColumn.Width.Value;
            }
            UpdateSidePanels();
        }
        else if (e.PropertyName == "RightPanelExpanded")
        {
            if (!_settings.EditorSettings.RightPanelExpanded)
            {
                // Record the current width before collapsing the panel
                _settings.EditorSettings.RightPanelWidth = (float)RightPanelColumn.Width.Value;
            }
            UpdateSidePanels();
        }
        else if (e.PropertyName == "BottomPanelExpanded")
        {
            if (!_settings.EditorSettings.BottomPanelExpanded)
            {
                // Record the current height before collapsing the panel
                _settings.EditorSettings.BottomPanelHeight = (float)BottomPanelRow.Height.Value;
            }
            UpdateSidePanels();
        }
    }

    private void UpdateSidePanels()
    {
        Guard.IsNotNull(_settings);
        Guard.IsNotNull(_settings.EditorSettings);

        // The trick here is to set the panel to collapsed _before_ setting the width to 0.
        // This avoids an exception in Skia.Gtk where it tries to perform layout on a 0 sized control.

        var leftPanelExpanded = _settings.EditorSettings.LeftPanelExpanded;
        if (leftPanelExpanded)
        {
            LeftSplitter.Visibility = Visibility.Visible;
            LeftPanel.Visibility = Visibility.Visible;
            LeftPanelColumn.MinWidth = 100;
            LeftPanelColumn.Width = new GridLength(_settings.EditorSettings.LeftPanelWidth);
        }
        else
        {
            LeftSplitter.Visibility = Visibility.Collapsed;
            LeftPanel.Visibility = Visibility.Collapsed;
            LeftPanelColumn.MinWidth = 0;
            LeftPanelColumn.Width = new GridLength(0);
        }

        var rightPanelExpanded = _settings.EditorSettings.RightPanelExpanded;
        if (rightPanelExpanded)
        {
            RightSplitter.Visibility = Visibility.Visible;
            RightPanel.Visibility = Visibility.Visible;
            RightPanelColumn.MinWidth = 100;
            RightPanelColumn.Width = new GridLength(_settings.EditorSettings.RightPanelWidth);
        }
        else
        {
            RightSplitter.Visibility = Visibility.Collapsed;
            RightPanel.Visibility = Visibility.Collapsed;
            RightPanelColumn.MinWidth = 0;
            RightPanelColumn.Width = new GridLength(0);
        }

        var bottomPanelExpanded = _settings.EditorSettings.BottomPanelExpanded;
        if (bottomPanelExpanded)
        {
            BottomSplitter.Visibility = Visibility.Visible;
            BottomPanel.Visibility = Visibility.Visible;
            BottomPanelRow.MinHeight = 100;
            BottomPanelRow.Height = new GridLength(_settings.EditorSettings.BottomPanelHeight);
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

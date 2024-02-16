using Celbridge.BaseLibrary.Settings;
using Celbridge.CommonUI.Messages;
using Celbridge.CommonUI.UserInterface;

namespace Celbridge.CommonUI.Views;

public sealed partial class WorkspaceView : Page
{
    private readonly IMessengerService _messengerService;
    private readonly IEditorSettings _settings;
    private readonly IUserInterfaceService _userInterfaceService;

    public WorkspaceViewModel ViewModel { get; set; }

    public WorkspaceView()
    {
        this.InitializeComponent();

        var serviceProvider = BaseLibrary.Core.Services.ServiceProvider;

        _messengerService = serviceProvider.GetRequiredService<IMessengerService>();
        _settings = serviceProvider.GetRequiredService<IEditorSettings>();
        _userInterfaceService = serviceProvider.GetRequiredService<IUserInterfaceService>();

        ViewModel = serviceProvider.GetRequiredService<WorkspaceViewModel>();

        Loaded += OnWorkspaceView_Loaded;
        Unloaded += OnWorkspaceView_Unloaded;
    }

    private void OnWorkspaceView_Loaded(object? sender, RoutedEventArgs e)
    {
#if WINDOWS
        // Setup the custom title bar (Windows only)

        var serviceProvider = BaseLibrary.Core.Services.ServiceProvider;
        var titleBar = serviceProvider.GetRequiredService<TitleBar>();
        LayoutRoot.Children.Add(titleBar);

        var mainWindow = _userInterfaceService.MainWindow;
        mainWindow.ExtendsContentIntoTitleBar = true;
        mainWindow.SetTitleBar(titleBar);
#endif

        _settings.PropertyChanged += OnSettings_PropertyChanged;

        _messengerService.Register<MainWindowActivated>(this, OnMainWindowActivated);

        LeftSplitter.SizeChanged += OnLeftSplitter_SizeChanged;
        RightSplitter.SizeChanged += OnRightSplitter_SizeChanged;
        CenterPanelGrid.LayoutUpdated += OnCenterPanelGrid_LayoutUpdated;

        UpdateSidePanels();

        // Notify listeners that the Workspace View has been loaded

        var message = new WorkspaceViewLoadedMessage(this);
        _messengerService.Send(message);
    }

    private void OnWorkspaceView_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unregister all event handlers to avoid memory leaks

        Loaded -= OnWorkspaceView_Loaded;
        Unloaded -= OnWorkspaceView_Unloaded;

        _settings.PropertyChanged -= OnSettings_PropertyChanged;

        _messengerService.Unregister<MainWindowActivated>(this);

        LeftSplitter.SizeChanged -= OnLeftSplitter_SizeChanged;
        RightSplitter.SizeChanged -= OnRightSplitter_SizeChanged;
        CenterPanelGrid.LayoutUpdated -= OnCenterPanelGrid_LayoutUpdated;

        // Notify listeners that the Workspace View has been unloaded

        var message = new WorkspaceViewUnloadedMessage();
        _messengerService.Send(message);
    }

    private void OnMainWindowActivated(object recipient, MainWindowActivated message)
    {
        UpdateSidePanels();
    }

    private void OnLeftSplitter_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _settings.LeftPanelWidth = (float)e.NewSize.Width;
    }

    private void OnRightSplitter_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _settings.RightPanelWidth = (float)e.NewSize.Width;
    }

    private void OnCenterPanelGrid_LayoutUpdated(object? sender, object e)
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

    private void OnSettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IEditorSettings.LeftPanelExpanded))
        {
            if (!_settings.LeftPanelExpanded)
            {
                // Record the current width before collapsing the panel
                _settings.LeftPanelWidth = (float)LeftPanelColumn.Width.Value;
            }
            UpdateSidePanels();
        }
        else if (e.PropertyName == nameof(IEditorSettings.RightPanelExpanded))
        {
            if (!_settings.RightPanelExpanded)
            {
                // Record the current width before collapsing the panel
                _settings.RightPanelWidth = (float)RightPanelColumn.Width.Value;
            }
            UpdateSidePanels();
        }
        else if (e.PropertyName == nameof(IEditorSettings.BottomPanelExpanded))
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

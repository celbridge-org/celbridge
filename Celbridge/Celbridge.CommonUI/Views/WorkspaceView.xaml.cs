using Celbridge.BaseLibrary.Settings;
using Celbridge.CommonUI.Messages;
using Celbridge.CommonUI.UserInterface;

namespace Celbridge.CommonUI.Views;

public sealed partial class WorkspaceView : Page
{
    private readonly IMessengerService _messengerService;
    private readonly IUserInterfaceService _userInterfaceService;

    public WorkspaceViewModel ViewModel { get; set; }

    public WorkspaceView()
    {
        this.InitializeComponent();

        var serviceProvider = BaseLibrary.Core.Services.ServiceProvider;

        _messengerService = serviceProvider.GetRequiredService<IMessengerService>();
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

        ViewModel.PropertyChanged += OnSettings_PropertyChanged;

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

        ViewModel.PropertyChanged -= OnSettings_PropertyChanged;
        ViewModel.OnView_Unloaded();

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
        ViewModel.LeftPanelWidth = (float)e.NewSize.Width;
    }

    private void OnRightSplitter_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ViewModel.RightPanelWidth = (float)e.NewSize.Width;
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
            ViewModel.LeftPanelWidth = leftWidth;
        }

        var rightWidth = (float)RightPanelColumn.Width.Value;
        if (rightWidth > RightPanelColumn.MinWidth)
        {
            ViewModel.RightPanelWidth = rightWidth;
        }

        var bottomHeight = (float)BottomPanelRow.Height.Value;
        if (bottomHeight > BottomPanelRow.MinHeight)
        {
            ViewModel.BottomPanelHeight = bottomHeight;
        }
    }

    private void OnSettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IEditorSettings.LeftPanelExpanded))
        {
            if (!ViewModel.LeftPanelExpanded)
            {
                // Record the current width before collapsing the panel
                ViewModel.LeftPanelWidth = (float)LeftPanelColumn.Width.Value;
            }
            UpdateSidePanels();
        }
        else if (e.PropertyName == nameof(IEditorSettings.RightPanelExpanded))
        {
            if (!ViewModel.RightPanelExpanded)
            {
                // Record the current width before collapsing the panel
                ViewModel.RightPanelWidth = (float)RightPanelColumn.Width.Value;
            }
            UpdateSidePanels();
        }
        else if (e.PropertyName == nameof(IEditorSettings.BottomPanelExpanded))
        {
            if (!ViewModel.BottomPanelExpanded)
            {
                // Record the current height before collapsing the panel
                ViewModel.BottomPanelHeight = (float)BottomPanelRow.Height.Value;
            }
            UpdateSidePanels();
        }
    }

    private void UpdateSidePanels()
    {
        // The trick here is to set the panel to collapsed _before_ setting the width to 0.
        // This avoids an exception in Skia.Gtk where it tries to perform layout on a 0 sized control.

        var leftPanelExpanded = ViewModel.LeftPanelExpanded;
        if (leftPanelExpanded)
        {
            LeftSplitter.Visibility = Visibility.Visible;
            LeftPanel.Visibility = Visibility.Visible;
            LeftPanelColumn.MinWidth = 100;
            LeftPanelColumn.Width = new GridLength(ViewModel.LeftPanelWidth);
        }
        else
        {
            LeftSplitter.Visibility = Visibility.Collapsed;
            LeftPanel.Visibility = Visibility.Collapsed;
            LeftPanelColumn.MinWidth = 0;
            LeftPanelColumn.Width = new GridLength(0);
        }

        var rightPanelExpanded = ViewModel.RightPanelExpanded;
        if (rightPanelExpanded)
        {
            RightSplitter.Visibility = Visibility.Visible;
            RightPanel.Visibility = Visibility.Visible;
            RightPanelColumn.MinWidth = 100;
            RightPanelColumn.Width = new GridLength(ViewModel.RightPanelWidth);
        }
        else
        {
            RightSplitter.Visibility = Visibility.Collapsed;
            RightPanel.Visibility = Visibility.Collapsed;
            RightPanelColumn.MinWidth = 0;
            RightPanelColumn.Width = new GridLength(0);
        }

        var bottomPanelExpanded = ViewModel.BottomPanelExpanded;
        if (bottomPanelExpanded)
        {
            BottomSplitter.Visibility = Visibility.Visible;
            BottomPanel.Visibility = Visibility.Visible;
            BottomPanelRow.MinHeight = 100;
            BottomPanelRow.Height = new GridLength(ViewModel.BottomPanelHeight);
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

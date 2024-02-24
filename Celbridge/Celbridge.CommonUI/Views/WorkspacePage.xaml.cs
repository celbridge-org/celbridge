using Celbridge.BaseLibrary.Settings;
using Celbridge.CommonServices.Messaging;

namespace Celbridge.CommonUI.Views;

public sealed partial class WorkspacePage : Page
{
    private readonly IMessengerService _messengerService;

    public WorkspacePageViewModel ViewModel { get; set; }

    public WorkspacePage()
    {
        this.InitializeComponent();

        var serviceProvider = Services.ServiceProvider;

        _messengerService = serviceProvider.GetRequiredService<IMessengerService>();
        ViewModel = serviceProvider.GetRequiredService<WorkspacePageViewModel>();

        Loaded += OnWorkspacePage_Loaded;
        Unloaded += OnWorkspacePage_Unloaded;
    }

    private void OnWorkspacePage_Loaded(object? sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnSettings_PropertyChanged;

        _messengerService.Register<MainWindowActivated>(this, OnMainWindowActivated);

        LeftSplitter.SizeChanged += OnLeftSplitter_SizeChanged;
        RightSplitter.SizeChanged += OnRightSplitter_SizeChanged;
        CenterPanelGrid.LayoutUpdated += OnCenterPanelGrid_LayoutUpdated;

        UpdateSidePanels();

        // Notify listeners that the page has been loaded

        var message = new PageLoadedMessage(this);
        _messengerService.Send(message);
    }

    private void OnWorkspacePage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unregister all event handlers to avoid memory leaks

        Loaded -= OnWorkspacePage_Loaded;
        Unloaded -= OnWorkspacePage_Unloaded;

        ViewModel.PropertyChanged -= OnSettings_PropertyChanged;
        ViewModel.OnView_Unloaded();

        _messengerService.Unregister<MainWindowActivated>(this);

        LeftSplitter.SizeChanged -= OnLeftSplitter_SizeChanged;
        RightSplitter.SizeChanged -= OnRightSplitter_SizeChanged;
        CenterPanelGrid.LayoutUpdated -= OnCenterPanelGrid_LayoutUpdated;

        // Notify listeners that the page has been loaded

        var message = new PageUnloadedMessage(this);
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

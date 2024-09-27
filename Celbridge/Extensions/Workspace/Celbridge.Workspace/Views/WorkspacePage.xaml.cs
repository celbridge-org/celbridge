using Celbridge.Messaging;
using Celbridge.Workspace.Services;
using Celbridge.Workspace.ViewModels;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Workspace.Views;

public sealed partial class WorkspacePage : Page
{
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;

    public WorkspacePageViewModel ViewModel { get; }

    public WorkspacePage()
    {
        InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<WorkspacePageViewModel>();

        _messengerService = serviceProvider.GetRequiredService<IMessengerService>();
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        ApplyPanelButtonTooltips();

        DataContext = ViewModel;

        Loaded += WorkspacePage_Loaded;
        Unloaded += WorkspacePage_Unloaded;
    }

    private void ApplyPanelButtonTooltips()
    {
        // Explorer panel 
        ToolTipService.SetToolTip(ShowExplorerPanelButton, _stringLocalizer["WorkspacePage_ShowPanelTooltip"]);
        ToolTipService.SetPlacement(ShowExplorerPanelButton, PlacementMode.Bottom);
        ToolTipService.SetToolTip(HideExplorerPanelButton, _stringLocalizer["WorkspacePage_HidePanelTooltip"]);
        ToolTipService.SetPlacement(HideExplorerPanelButton, PlacementMode.Bottom);

        // Inspector panel 
        ToolTipService.SetToolTip(ShowInspectorPanelButton, _stringLocalizer["WorkspacePage_ShowPanelTooltip"]);
        ToolTipService.SetPlacement(ShowInspectorPanelButton, PlacementMode.Bottom);
        ToolTipService.SetToolTip(HideInspectorPanelButton, _stringLocalizer["WorkspacePage_HidePanelTooltip"]);
        ToolTipService.SetPlacement(HideInspectorPanelButton, PlacementMode.Bottom);

        // Tools panel 
        ToolTipService.SetToolTip(ShowToolsPanelButton, _stringLocalizer["WorkspacePage_ShowPanelTooltip"]);
        ToolTipService.SetPlacement(ShowToolsPanelButton, PlacementMode.Top);
        ToolTipService.SetToolTip(HideToolsPanelButton, _stringLocalizer["WorkspacePage_HidePanelTooltip"]);
        ToolTipService.SetPlacement(HideToolsPanelButton, PlacementMode.Top);

        // Focus mode button
        ToolTipService.SetToolTip(EnterFocusModeButton, _stringLocalizer["WorkspacePage_EnterFocusModeTooltip"]);
        ToolTipService.SetPlacement(EnterFocusModeButton, PlacementMode.Top);
        ToolTipService.SetToolTip(ExitFocusModeButton, _stringLocalizer["WorkspacePage_ExitFocusModeTooltip"]);
        ToolTipService.SetPlacement(ExitFocusModeButton, PlacementMode.Top);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.LoadProjectCancellationToken = e.Parameter as CancellationTokenSource;
    }

    private void WorkspacePage_Loaded(object sender, RoutedEventArgs e)
    {
        var leftPanelWidth = ViewModel.ExplorerPanelWidth;
        var rightPanelWidth = ViewModel.InspectorPanelWidth;
        var bottomPanelHeight = ViewModel.ToolsPanelHeight;

        if (leftPanelWidth > 0)
        {
            ExplorerPanelColumn.Width = new GridLength(leftPanelWidth);
        }
        if (rightPanelWidth > 0)
        {
            InspectorPanelColumn.Width = new GridLength(rightPanelWidth);
        }
        if (bottomPanelHeight > 0)
        {
            ToolsPanelRow.Height = new GridLength(bottomPanelHeight);
        }

        UpdatePanels();
        UpdateFocusModeButton();

        ExplorerPanel.SizeChanged += (s, e) => ViewModel.ExplorerPanelWidth = (float)e.NewSize.Width;
        InspectorPanel.SizeChanged += (s, e) => ViewModel.InspectorPanelWidth = (float)e.NewSize.Width;
        ToolsPanel.SizeChanged += (s, e) => ViewModel.ToolsPanelHeight = (float)e.NewSize.Height;

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        //
        // Populate the workspace panels.
        //

        var serviceProvider = ServiceLocator.ServiceProvider;
        var workspaceWrapper = serviceProvider.GetRequiredService<IWorkspaceWrapper>();
        var workspaceService = workspaceWrapper.WorkspaceService as WorkspaceService;
        Guard.IsNotNull(workspaceService);

        // Send a message to tell services to initialize their workspace panels
        var message = new WorkspaceWillPopulatePanelsMessage();
        _messengerService.Send(message);

        // Insert the child panels at the start of the children collection so that the panel toggle
        // buttons take priority for accepting input.

        var explorerPanel = workspaceService.ExplorerService.ExplorerPanel as UIElement;
        ExplorerPanel.Children.Insert(0, explorerPanel);

        var documentsPanel = workspaceService.DocumentsService.DocumentsPanel as UIElement;
        DocumentsPanel.Children.Insert(0, documentsPanel);

        var inspectorPanel = workspaceService.InspectorService.InspectorPanel as UIElement;
        InspectorPanel.Children.Add(inspectorPanel);

        var consolePanel = workspaceService.ConsoleService.ConsolePanel as UIElement;
        ToolsPanel.Children.Insert(0, consolePanel);

        var statusPanel = workspaceService.StatusService.StatusPanel as UIElement;
        StatusPanel.Children.Add(statusPanel);

        _ = ViewModel.LoadWorkspaceAsync();
    }

    private void WorkspacePage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.OnWorkspacePageUnloaded();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.IsExplorerPanelVisible):
            case nameof(ViewModel.IsInspectorPanelVisible):
            case nameof(ViewModel.IsToolsPanelVisible):
                UpdatePanels();
                break;
            case nameof(ViewModel.IsFocusModeActive):
                UpdateFocusModeButton();
                break;
        }
    }

    private void UpdatePanels()
    {
        //
        // Update button visibility based on panel visibility state
        //

        ShowExplorerPanelButton.Visibility = ViewModel.IsExplorerPanelVisible ? Visibility.Collapsed : Visibility.Visible;
        HideExplorerPanelButton.Visibility = ViewModel.IsExplorerPanelVisible ? Visibility.Visible : Visibility.Collapsed;

        ShowInspectorPanelButton.Visibility = ViewModel.IsInspectorPanelVisible ? Visibility.Collapsed : Visibility.Visible;
        HideInspectorPanelButton.Visibility = ViewModel.IsInspectorPanelVisible ? Visibility.Visible : Visibility.Collapsed;

        ShowToolsPanelButton.Visibility = ViewModel.IsToolsPanelVisible ? Visibility.Collapsed : Visibility.Visible;
        HideToolsPanelButton.Visibility = ViewModel.IsToolsPanelVisible ? Visibility.Visible : Visibility.Collapsed;

        //
        // Update panel and splitter visibility based on the panel visibility state
        //

        if (ViewModel.IsExplorerPanelVisible)
        {
            ExplorerPanelSplitter.Visibility = Visibility.Visible;
            ExplorerPanel.Visibility = Visibility.Visible;
            ExplorerPanelColumn.MinWidth = 100;
            ExplorerPanelColumn.Width = new GridLength(ViewModel.ExplorerPanelWidth);
        }
        else
        {
            ExplorerPanelSplitter.Visibility = Visibility.Collapsed;
            ExplorerPanel.Visibility = Visibility.Collapsed;
            ExplorerPanelColumn.MinWidth = 0;
            ExplorerPanelColumn.Width = new GridLength(0);
        }

        if (ViewModel.IsInspectorPanelVisible)
        {
            InspectorPanelSplitter.Visibility = Visibility.Visible;
            InspectorPanel.Visibility = Visibility.Visible;
            InspectorPanelColumn.MinWidth = 100;
            InspectorPanelColumn.Width = new GridLength(ViewModel.InspectorPanelWidth);
        }
        else
        {
            InspectorPanelSplitter.Visibility = Visibility.Collapsed;
            InspectorPanel.Visibility = Visibility.Collapsed;
            InspectorPanelColumn.MinWidth = 0;
            InspectorPanelColumn.Width = new GridLength(0);
        }

        if (ViewModel.IsToolsPanelVisible)
        {
            ToolsPanelSplitter.Visibility = Visibility.Visible;
            ToolsPanel.Visibility = Visibility.Visible;
            ToolsPanelRow.MinHeight = 100;
            ToolsPanelRow.Height = new GridLength(ViewModel.ToolsPanelHeight);
        }
        else
        {
            ToolsPanelSplitter.Visibility = Visibility.Collapsed;
            ToolsPanel.Visibility = Visibility.Collapsed;
            ToolsPanelRow.MinHeight = 0;
            ToolsPanelRow.Height = new GridLength(0);
        }
    }

    private void UpdateFocusModeButton()
    {
        if (ViewModel.IsFocusModeActive)
        {
            // Show the exit focus mode button
            EnterFocusModeButton.Visibility = Visibility.Collapsed;
            ExitFocusModeButton.Visibility = Visibility.Visible;
        }
        else
        {
            // Show the enter focus mode button
            EnterFocusModeButton.Visibility = Visibility.Visible;
            ExitFocusModeButton.Visibility = Visibility.Collapsed;
        }
    }
}

using Celbridge.Workspace.Services;
using Celbridge.Workspace.ViewModels;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Workspace.Views;

public sealed partial class WorkspacePage : Page
{
    private readonly IStringLocalizer _stringLocalizer;

    public WorkspacePageViewModel ViewModel { get; }

    public WorkspacePage()
    {
        InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<WorkspacePageViewModel>();

        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        ApplyPanelButtonTooltips();

        DataContext = ViewModel;

        Loaded += WorkspacePage_Loaded;
        Unloaded += WorkspacePage_Unloaded;
    }

    private void ApplyPanelButtonTooltips()
    {
        // Left panel 
        ToolTipService.SetToolTip(ShowLeftPanelButton, _stringLocalizer["WorkspacePage_ShowPanelTooltip"]);
        ToolTipService.SetPlacement(ShowLeftPanelButton, PlacementMode.Bottom);
        ToolTipService.SetToolTip(HideLeftPanelButton, _stringLocalizer["WorkspacePage_HidePanelTooltip"]);
        ToolTipService.SetPlacement(HideLeftPanelButton, PlacementMode.Bottom);

        // Right panel 
        ToolTipService.SetToolTip(ShowRightPanelButton, _stringLocalizer["WorkspacePage_ShowPanelTooltip"]);
        ToolTipService.SetPlacement(ShowRightPanelButton, PlacementMode.Bottom);
        ToolTipService.SetToolTip(HideRightPanelButton, _stringLocalizer["WorkspacePage_HidePanelTooltip"]);
        ToolTipService.SetPlacement(HideRightPanelButton, PlacementMode.Bottom);

        // Bottom panel 
        ToolTipService.SetToolTip(ShowBottomPanelButton, _stringLocalizer["WorkspacePage_ShowPanelTooltip"]);
        ToolTipService.SetPlacement(ShowBottomPanelButton, PlacementMode.Top);
        ToolTipService.SetToolTip(HideBottomPanelButton, _stringLocalizer["WorkspacePage_HidePanelTooltip"]);
        ToolTipService.SetPlacement(HideBottomPanelButton, PlacementMode.Top);

        // Show / Hide all buttons
        ToolTipService.SetToolTip(ShowAllPanelsButton, _stringLocalizer["WorkspacePage_ShowAllPanelsTooltip"]);
        ToolTipService.SetPlacement(ShowAllPanelsButton, PlacementMode.Top);
        ToolTipService.SetToolTip(HideAllPanelsButton, _stringLocalizer["WorkspacePage_HideAllPanelsTooltip"]);
        ToolTipService.SetPlacement(HideAllPanelsButton, PlacementMode.Top);

    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.LoadProjectCancellationToken = e.Parameter as CancellationTokenSource;
    }

    private void WorkspacePage_Loaded(object sender, RoutedEventArgs e)
    {
        var leftPanelWidth = ViewModel.LeftPanelWidth;
        var rightPanelWidth = ViewModel.RightPanelWidth;
        var bottomPanelHeight = ViewModel.BottomPanelHeight;

        if (leftPanelWidth > 0)
        {
            LeftPanelColumn.Width = new GridLength(leftPanelWidth);
        }
        if (rightPanelWidth > 0)
        {
            RightPanelColumn.Width = new GridLength(rightPanelWidth);
        }
        if (bottomPanelHeight > 0)
        {
            BottomPanelRow.Height = new GridLength(bottomPanelHeight);
        }

        UpdatePanels();
        UpdateToggleAllPanelsButton();

        LeftPanel.SizeChanged += (s, e) => ViewModel.LeftPanelWidth = (float)e.NewSize.Width;
        RightPanel.SizeChanged += (s, e) => ViewModel.RightPanelWidth = (float)e.NewSize.Width;
        BottomPanel.SizeChanged += (s, e) => ViewModel.BottomPanelHeight = (float)e.NewSize.Height;

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        //
        // Populate the workspace panels.
        //

        var serviceProvider = ServiceLocator.ServiceProvider;
        var workspaceWrapper = serviceProvider.GetRequiredService<IWorkspaceWrapper>();
        var workspaceService = workspaceWrapper.WorkspaceService as WorkspaceService;
        Guard.IsNotNull(workspaceService);

        // Insert the child panels at the start of the children collection so that the panel toggle
        // buttons take priority for accepting input.

        var consolePanel = workspaceService.ConsoleService.CreateConsolePanel() as UIElement;
        BottomPanel.Children.Insert(0, consolePanel);

        var documentsPanel = workspaceService.DocumentsService.CreateDocumentsPanel() as UIElement;
        CenterPanel.Children.Insert(0, documentsPanel);

        var inspectorPanel = workspaceService.InspectorService.CreateInspectorPanel() as UIElement;
        RightPanel.Children.Add(inspectorPanel);

        var explorerPanel = workspaceService.ExplorerService.CreateExplorerPanel() as UIElement;
        LeftPanel.Children.Insert(0, explorerPanel);

        var statusPanel = workspaceService.StatusService.CreateStatusPanel() as UIElement;
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
            case nameof(ViewModel.IsLeftPanelVisible):
            case nameof(ViewModel.IsRightPanelVisible):
            case nameof(ViewModel.IsBottomPanelVisible):
                UpdatePanels();
                break;
            case nameof(ViewModel.AllPanelsVisible):
                UpdateToggleAllPanelsButton();
                break;
        }
    }

    private void UpdatePanels()
    {
        //
        // Update button visibility based on panel visibility state
        //

        ShowLeftPanelButton.Visibility = ViewModel.IsLeftPanelVisible ? Visibility.Collapsed : Visibility.Visible;
        HideLeftPanelButton.Visibility = ViewModel.IsLeftPanelVisible ? Visibility.Visible : Visibility.Collapsed;

        ShowRightPanelButton.Visibility = ViewModel.IsRightPanelVisible ? Visibility.Collapsed : Visibility.Visible;
        HideRightPanelButton.Visibility = ViewModel.IsRightPanelVisible ? Visibility.Visible : Visibility.Collapsed;

        ShowBottomPanelButton.Visibility = ViewModel.IsBottomPanelVisible ? Visibility.Collapsed : Visibility.Visible;
        HideBottomPanelButton.Visibility = ViewModel.IsBottomPanelVisible ? Visibility.Visible : Visibility.Collapsed;

        //
        // Update panel and splitter visibility based on the panel visibility state
        //

        if (ViewModel.IsLeftPanelVisible)
        {
            LeftPanelSplitter.Visibility = Visibility.Visible;
            LeftPanel.Visibility = Visibility.Visible;
            LeftPanelColumn.MinWidth = 100;
            LeftPanelColumn.Width = new GridLength(ViewModel.LeftPanelWidth);
        }
        else
        {
            LeftPanelSplitter.Visibility = Visibility.Collapsed;
            LeftPanel.Visibility = Visibility.Collapsed;
            LeftPanelColumn.MinWidth = 0;
            LeftPanelColumn.Width = new GridLength(0);
        }

        if (ViewModel.IsRightPanelVisible)
        {
            RightPanelSplitter.Visibility = Visibility.Visible;
            RightPanel.Visibility = Visibility.Visible;
            RightPanelColumn.MinWidth = 100;
            RightPanelColumn.Width = new GridLength(ViewModel.RightPanelWidth);
        }
        else
        {
            RightPanelSplitter.Visibility = Visibility.Collapsed;
            RightPanel.Visibility = Visibility.Collapsed;
            RightPanelColumn.MinWidth = 0;
            RightPanelColumn.Width = new GridLength(0);
        }

        if (ViewModel.IsBottomPanelVisible)
        {
            BottomPanelSplitter.Visibility = Visibility.Visible;
            BottomPanel.Visibility = Visibility.Visible;
            BottomPanelRow.MinHeight = 100;
            BottomPanelRow.Height = new GridLength(ViewModel.BottomPanelHeight);
        }
        else
        {
            BottomPanelSplitter.Visibility = Visibility.Collapsed;
            BottomPanel.Visibility = Visibility.Collapsed;
            BottomPanelRow.MinHeight = 0;
            BottomPanelRow.Height = new GridLength(0);
        }
    }

    private void UpdateToggleAllPanelsButton()
    {
        if (ViewModel.AllPanelsVisible)
        {
            ShowAllPanelsButton.Visibility = Visibility.Collapsed;
            HideAllPanelsButton.Visibility = Visibility.Visible;
        }
        else
        {
            ShowAllPanelsButton.Visibility = Visibility.Visible;
            HideAllPanelsButton.Visibility = Visibility.Collapsed;
        }
    }
}

using Celbridge.Commands;
using Celbridge.Community;
using Celbridge.Documents;
using Celbridge.Explorer;
using Celbridge.Packages;
using Celbridge.ProjectSettings;
using Celbridge.Search;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.WorkspaceUI.ViewModels;
using Celbridge.WorkspaceUI.Views.Controls;
using Microsoft.Extensions.Localization;
using Windows.Foundation;

namespace Celbridge.WorkspaceUI.Views;

/// <summary>
/// Pairs a community link with the rail button that opens it.
/// </summary>
internal sealed record CommunityRailButton(CommunityLink Link, UtilityButton Button);

public sealed partial class UtilityPanel : UserControl, IUtilityPanel
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IFocusService _focusService;
    private readonly ISettingsService _settings;
    private readonly IMessengerService _messengerService;
    private readonly ISpotlightRegistry _spotlightRegistry;
    private readonly ICommandService _commandService;

    // Spotlight landmark ids for the built-in rail buttons. These must match the descriptors seeded in
    // SpotlightLandmarks exactly.
    private const string ExplorerLandmarkId = "explorer-utility-button";
    private const string SearchLandmarkId = "search-utility-button";
    private const string ProjectSettingsLandmarkId = "project-settings-utility-button";

    // Rail buttons, content hosts, and focus callbacks for every surface (built-in and custom), keyed by
    // utility id. The view owns content hosting and focus acquisition. The view model owns the rail selection
    // and focus state, which the buttons bind to.
    private readonly Dictionary<EditorId, UtilityButton> _buttons = new();
    private readonly Dictionary<EditorId, ContentControl> _contentControls = new();
    private readonly Dictionary<EditorId, Action> _focusActions = new();

    // Docked utilities (utility id -> the document resource its WebView is docked into). A docked utility's rail
    // click activates its document tab instead of showing the panel surface.
    private readonly Dictionary<EditorId, ResourceKey> _dockedUtilityResources = new();

    // The community link buttons, kept so their tooltips can be applied once the panel loads. They are not rail
    // items: they never select a surface, so they carry no selection, focus, or docked state.
    private readonly List<CommunityRailButton> _communityButtons = new();

    // Selection is persisted only after RestoreSelectedUtility runs, so the constructor's default selection and
    // the restore itself do not overwrite the saved selection before it is read.
    private bool _selectionPersistenceEnabled;

    public IExplorerPanel ExplorerPanel { get; }
    public ISearchPanel SearchPanel { get; }
    public IProjectSettingsPanel ProjectSettingsPanel { get; }

    public UtilityPanelViewModel ViewModel { get; }

    public EditorId ActiveUtilityId => ViewModel.SelectedUtilityId;

    public double MinimumWidth
    {
        get
        {
            // The content area is carved out like a document section, so it composes from the same floor.
            var contentChrome = new Size(
                ContentArea.BorderThickness.Left + ContentArea.BorderThickness.Right,
                ContentArea.BorderThickness.Top + ContentArea.BorderThickness.Bottom);
            double contentMinimumWidth = WorkspaceMinimumSize.ComposeSection(contentChrome).Width;

            return RailColumn.Width.Value + contentMinimumWidth;
        }
    }

    // Whether the panel draws a bottom edge and rounded bottom corners: it does when the Bottom document
    // area runs underneath it, and meets the application border flush otherwise. Driven by the surface
    // container, which owns the panel's placement.
    internal void SetBottomEdgePresented(bool isPresented)
    {
        double panelCornerRadius = (double)Application.Current.Resources["PanelCornerRadius"];
        double bottomRadius = isPresented ? panelCornerRadius : 0;

        ContentArea.BorderThickness = new Thickness(1, 1, 1, isPresented ? 1 : 0);
        ContentArea.CornerRadius = new CornerRadius(panelCornerRadius, panelCornerRadius, bottomRadius, bottomRadius);
    }

    public UtilityPanel()
    {
        this.InitializeComponent();

        SetBottomEdgePresented(false);

        RailColumn.Width = new GridLength(WorkspaceConstants.UtilityPanelRailWidth);

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _focusService = ServiceLocator.AcquireService<IFocusService>();
        _settings = ServiceLocator.AcquireService<ISettingsService>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _spotlightRegistry = ServiceLocator.AcquireService<ISpotlightRegistry>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();

        // Acquire panel views via DI and host them in ContentControls
        ExplorerPanel = ServiceLocator.AcquireService<IExplorerPanel>();
        SearchPanel = ServiceLocator.AcquireService<ISearchPanel>();
        ProjectSettingsPanel = ServiceLocator.AcquireService<IProjectSettingsPanel>();
        ExplorerPanelControl.Content = ExplorerPanel as UIElement;
        SearchPanelControl.Content = SearchPanel as UIElement;
        ProjectSettingsPanelControl.Content = ProjectSettingsPanel as UIElement;

        ViewModel = ServiceLocator.AcquireService<UtilityPanelViewModel>();
        DataContext = ViewModel;

        InitializeBuiltInButtons();
        InitializeCommunityButtons();

        // Show the Explorer surface by default
        ShowSurface(BuiltInUtilityIds.Explorer);

        Loaded += UtilityPanel_Loaded;
        Unloaded += UtilityPanel_Unloaded;
    }

    // Tooltips are applied later in ApplyTooltips, once the localizer strings are read.
    private void InitializeBuiltInButtons()
    {
        var explorerItem = ViewModel.AddItem(BuiltInUtilityIds.Explorer, WorkspacePanelId.Explorer);
        var searchItem = ViewModel.AddItem(BuiltInUtilityIds.Search, WorkspacePanelId.Search);

        ExplorerButton.SetIcon(IconSymbol.Folder);
        ExplorerButton.SetAutomationId(ExplorerLandmarkId);
        BindButton(ExplorerButton, explorerItem);
        ExplorerButton.Click += (sender, e) => ShowUtility(BuiltInUtilityIds.Explorer);

        SearchButton.SetIcon(IconSymbol.Search);
        SearchButton.SetAutomationId(SearchLandmarkId);
        BindButton(SearchButton, searchItem);
        SearchButton.Click += (sender, e) => ShowUtility(BuiltInUtilityIds.Search);

        var projectSettingsItem = ViewModel.AddItem(BuiltInUtilityIds.ProjectSettings, WorkspacePanelId.ProjectSettings);

        ProjectSettingsButton.SetIcon(IconSymbol.Sliders);
        ProjectSettingsButton.SetAutomationId(ProjectSettingsLandmarkId);
        BindButton(ProjectSettingsButton, projectSettingsItem);
        ProjectSettingsButton.Click += (sender, e) => ShowUtility(BuiltInUtilityIds.ProjectSettings);

        _buttons[BuiltInUtilityIds.Explorer] = ExplorerButton;
        _buttons[BuiltInUtilityIds.Search] = SearchButton;
        _buttons[BuiltInUtilityIds.ProjectSettings] = ProjectSettingsButton;
        _contentControls[BuiltInUtilityIds.Explorer] = ExplorerPanelControl;
        _contentControls[BuiltInUtilityIds.Search] = SearchPanelControl;
        _contentControls[BuiltInUtilityIds.ProjectSettings] = ProjectSettingsPanelControl;
        _focusActions[BuiltInUtilityIds.Explorer] = ExplorerPanel.FocusPanel;
        _focusActions[BuiltInUtilityIds.Search] = SearchPanel.FocusSearchInput;
        _focusActions[BuiltInUtilityIds.ProjectSettings] = ProjectSettingsPanel.FocusPanel;
    }

    private void InitializeCommunityButtons()
    {
        foreach (var link in CommunityLinks.All)
        {
            var railButton = new UtilityButton();
            railButton.SetIcon(link.Icon);
            railButton.SetAutomationId(link.LandmarkId);

            railButton.Click += (sender, e) => OpenCommunityLink(link);

            CommunityItems.Children.Add(railButton);

            _communityButtons.Add(new CommunityRailButton(link, railButton));
        }
    }

    private void OpenCommunityLink(CommunityLink link)
    {
        _commandService.Execute<IOpenCommunityLinkCommand>(command => command.LinkId = link.LinkId);
    }

    // Binds a rail button's visual state to its item view model.
    private static void BindButton(UtilityButton button, UtilityItemViewModel item)
    {
        button.SetBinding(UtilityButton.IsSelectedProperty, new Binding
        {
            Source = item,
            Path = new PropertyPath(nameof(UtilityItemViewModel.IsSelected)),
            Mode = BindingMode.OneWay
        });
        button.SetBinding(UtilityButton.IsFocusedProperty, new Binding
        {
            Source = item,
            Path = new PropertyPath(nameof(UtilityItemViewModel.IsFocused)),
            Mode = BindingMode.OneWay
        });
        button.SetBinding(UtilityButton.IsDockedProperty, new Binding
        {
            Source = item,
            Path = new PropertyPath(nameof(UtilityItemViewModel.IsDocked)),
            Mode = BindingMode.OneWay
        });
    }

    private void UtilityPanel_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTooltips();

        // Register how the hosted panels take keyboard focus, so the focus service can return focus to
        // whichever is focused after a modal dialog closes or the resource tree rebuilds.
        _focusService.SetPanelFocusHandler(WorkspacePanelId.Explorer, ExplorerPanel.FocusPanel);
        _focusService.SetPanelFocusHandler(WorkspacePanelId.Search, SearchPanel.FocusSearchInput);
        _focusService.SetPanelFocusHandler(WorkspacePanelId.ProjectSettings, ProjectSettingsPanel.FocusPanel);

        // The utility panels drop their own header focus indicator and show focus on the selected rail button
        // instead, so feed panel focus changes into the view model to colour the indicator accordingly.
        _messengerService.Register<PanelFocusChangedMessage>(this, OnPanelFocusChanged);
        ViewModel.ReconcileFocus(_focusService.FocusedPanel);

        // Package discovery is what produces contribution issues, so the rail pip refreshes whenever a
        // discovery pass completes.
        _messengerService.Register<PackagesInitializedMessage>(this, OnPackagesInitialized);
        UpdateProjectSettingsIssuePip();
    }

    private void UtilityPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        _messengerService.Unregister<PanelFocusChangedMessage>(this);
        _messengerService.Unregister<PackagesInitializedMessage>(this);
        _focusService.SetPanelFocusHandler(WorkspacePanelId.Explorer, null);
        _focusService.SetPanelFocusHandler(WorkspacePanelId.Search, null);
        _focusService.SetPanelFocusHandler(WorkspacePanelId.ProjectSettings, null);
    }

    private void OnPackagesInitialized(object recipient, PackagesInitializedMessage message)
    {
        UpdateProjectSettingsIssuePip();
    }

    // Flags the Project Settings rail button when any contribution has dropped configuration.
    private void UpdateProjectSettingsIssuePip()
    {
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        var packageService = workspaceWrapper.WorkspaceService?.PackageService;
        var hasIssues = packageService is not null
            && packageService.GetContributionIssues().Count > 0;

        ProjectSettingsButton.SetIssuePipVisible(hasIssues);
    }

    private void ApplyTooltips()
    {
        var explorerTooltip = _stringLocalizer.GetString("UtilityPanel_ExplorerTooltip");
        ExplorerButton.SetTooltip(explorerTooltip);

        var searchTooltip = _stringLocalizer.GetString("UtilityPanel_SearchTooltip");
        SearchButton.SetTooltip(searchTooltip);

        var projectSettingsTooltip = _stringLocalizer.GetString("UtilityPanel_ProjectSettingsTooltip");
        ProjectSettingsButton.SetTooltip(projectSettingsTooltip);

        foreach (var communityButton in _communityButtons)
        {
            var communityTooltip = _stringLocalizer.GetString(communityButton.Link.TooltipKey);
            communityButton.Button.SetTooltip(communityTooltip);
        }
    }

    private void OnPanelFocusChanged(object recipient, PanelFocusChangedMessage message)
    {
        ViewModel.ReconcileFocus(message.FocusedPanel);
    }

    public void ShowUtility(EditorId utilityId)
    {
        // A utility docked as a document activates its document tab, without changing the shown panel surface or
        // the rail highlight. A utility in the panel selects its rail surface.
        if (_dockedUtilityResources.TryGetValue(utilityId, out var documentResource))
        {
            // Activate the docked utility's tab, then request an attention flash so the reveal gives visible
            // feedback even when the tab was already the active document.
            _commandService.Execute<IActivateDocumentCommand>(command => command.FileResource = documentResource);
            _messengerService.Send(new FlashDocumentMessage(documentResource));
            return;
        }

        if (!_contentControls.ContainsKey(utilityId))
        {
            return;
        }

        // Re-read the project config each time Project Settings is shown so it reflects the on-disk file.
        if (utilityId == BuiltInUtilityIds.ProjectSettings)
        {
            ProjectSettingsPanel.Refresh();
        }

        // A lazy-load utility creates its WebView on first show. The surface is shown
        // immediately; the WebView attaches to it when initialization completes.
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        _ = workspaceWrapper.WorkspaceService.UtilityService.EnsureUtilityInitializedAsync(utilityId);

        ShowSurface(utilityId);
        PersistSelectedUtility(utilityId.ToString());
    }

    // Selects the surface in the view model (which lights the accent optimistically) and shows its content.
    private void ShowSurface(EditorId utilityId, bool takeFocus = true)
    {
        if (!_contentControls.TryGetValue(utilityId, out var content))
        {
            return;
        }

        ViewModel.SelectUtility(utilityId);
        ShowContent(utilityId, content, takeFocus);
        NotifyActiveUtilityChanged();
    }

    // Shows the incoming content on top and, once it has been laid out, focuses it and then collapses the other
    // content hosts. Keeping the outgoing content visible until focus has moved onto the incoming surface stops
    // WinUI from relocating focus to another panel when the previously focused element would otherwise be
    // collapsed. Focusing after layout (rather than this tick) lands on a control that is actually focusable.
    private void ShowContent(EditorId utilityId, ContentControl content, bool takeFocus)
    {
        // A surface that is already visible (re-selected while another panel holds focus, e.g. after a
        // docked utility moved focus to a document) is already laid out and setting it visible again may
        // raise no LayoutUpdated, so focus it now. Otherwise re-selecting it would never move focus back
        // onto it, leaving its rail button unfocused.
        bool wasAlreadyVisible = content.Visibility == Visibility.Visible;

        Canvas.SetZIndex(content, 1);
        content.Visibility = Visibility.Visible;

        if (wasAlreadyVisible)
        {
            FinishShowingContent(utilityId, content, takeFocus);
            return;
        }

        void OnLayoutUpdated(object? sender, object args)
        {
            content.LayoutUpdated -= OnLayoutUpdated;
            FinishShowingContent(utilityId, content, takeFocus);
        }

        content.LayoutUpdated += OnLayoutUpdated;
    }

    private void FinishShowingContent(EditorId utilityId, ContentControl content, bool takeFocus)
    {
        // Drop a stale attempt when a later selection superseded this one before layout ran.
        if (ViewModel.SelectedUtilityId != utilityId
            || content.Visibility != Visibility.Visible)
        {
            return;
        }

        if (takeFocus)
        {
            if (_focusActions.TryGetValue(utilityId, out var focusContent))
            {
                focusContent();
            }

            // A web-view utility's focusContent moves only native focus, so managed focus stays on the
            // outgoing surface. Collapsing that surface below would then relocate managed focus onto
            // unrelated chrome (a document tab), clobbering the web view's just-reported CustomUtility panel.
            // Yield managed focus to this utility's host - it carries the CustomUtility panel declaration - so
            // the collapse has nothing to relocate. Pointer state matches the focus the host receives
            // naturally when switching in from a managed panel, which leaves web-view typing working.
            if (IsCustomUtility(utilityId))
            {
                content.Focus(FocusState.Pointer);
            }
        }

        ViewModel.ReconcileFocus(_focusService.FocusedPanel);

        CollapseContentExcept(content);
    }

    private void CollapseContentExcept(ContentControl shown)
    {
        foreach (var content in _contentControls.Values)
        {
            if (!ReferenceEquals(content, shown))
            {
                content.Visibility = Visibility.Collapsed;
                Canvas.SetZIndex(content, 0);
            }
        }
    }

    // Broadcasts the now-active rail surface, so app-level state (e.g. app_get_state) can report it without
    // touching this UI object off the UI thread.
    private void NotifyActiveUtilityChanged()
    {
        _messengerService.Send(new ActiveUtilityChangedMessage(ActiveUtilityId.ToString()));
    }

    public void BuildCustomUtilities(IReadOnlyList<CustomUtility> utilities)
    {
        ClearCustomUtilities();

        foreach (var utility in utilities)
        {
            var item = ViewModel.AddItem(utility.UtilityId, WorkspacePanelId.CustomUtility);

            var railButton = new UtilityButton();
            railButton.SetIcon(utility.IconName);
            railButton.SetTooltip(utility.Tooltip);

            var landmarkId = CustomLandmarkId(utility.UtilityId);
            railButton.SetAutomationId(landmarkId);

            BindButton(railButton, item);

            var utilityId = utility.UtilityId;
            railButton.Click += (sender, e) => ShowUtility(utilityId);

            RailItems.Children.Add(railButton);

            _spotlightRegistry.RegisterLandmark(new LandmarkDescriptor(landmarkId, WorkspaceSurface.UtilityPanel));

            var contentControl = new ContentControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Collapsed,
                Content = utility.Content as UIElement
            };

            // Declare the panel on the host wrapper, not just the custom utility view inside it. Focusing
            // the hosted web view lands managed focus on this ContentControl, and the focus tracker
            // classifies by walking towards the root, so without a declaration here the walk passes the
            // view's own declaration and reports None - clearing the rail button's focus highlight.
            FocusTracking.SetPanel(contentControl, WorkspacePanelId.CustomUtility);

            ContentArea.Children.Add(contentControl);

            _buttons[utility.UtilityId] = railButton;
            _contentControls[utility.UtilityId] = contentControl;
            _focusActions[utility.UtilityId] = utility.FocusPanel;
        }
    }

    public void ClearCustomUtilities()
    {
        // This runs on unload and on rebuild, so the revert-to-Explorer below must not persist over the user's
        // saved selection. RestoreSelectedUtility re-enables persistence once the rebuilt rail is restored.
        _selectionPersistenceEnabled = false;

        // Revert to Explorer before removing items so a removed utility is never left showing or highlighted.
        if (IsCustomSurfaceSelected())
        {
            ShowSurface(BuiltInUtilityIds.Explorer);
        }

        foreach (var utilityId in GetCustomUtilityIds())
        {
            var railButton = _buttons[utilityId];
            RailItems.Children.Remove(railButton);

            var contentControl = _contentControls[utilityId];
            contentControl.Content = null;
            ContentArea.Children.Remove(contentControl);

            _spotlightRegistry.UnregisterLandmark(CustomLandmarkId(utilityId));

            _buttons.Remove(utilityId);
            _contentControls.Remove(utilityId);
            _focusActions.Remove(utilityId);
            _dockedUtilityResources.Remove(utilityId);
            ViewModel.RemoveItem(utilityId);
        }
    }

    public void SetUtilityDockLocation(EditorId utilityId, DockLocation location, ResourceKey documentResource)
    {
        bool isDocument = location == DockLocation.Document;
        if (isDocument)
        {
            _dockedUtilityResources[utilityId] = documentResource;
        }
        else
        {
            _dockedUtilityResources.Remove(utilityId);
        }

        ViewModel.SetDocked(utilityId, isDocument);
    }

    public void FlashUtility(EditorId utilityId)
    {
        if (!_buttons.TryGetValue(utilityId, out var button))
        {
            return;
        }

        // Deferred to a low dispatcher tick so the undock reparent settles before the button pulses.
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => button.FlashAttention());
    }

    public void RestoreSelectedUtility()
    {
        var tag = _settings.Get(SettingCatalog.Layout.UtilityPanelSelectedUtility);

        // Restoring the previously selected utility shows its surface; it is not the user choosing to work
        // in that panel, so it claims the keyboard only as a fallback. The restored active document takes
        // focus first and keeps it, and a workspace with no open document leaves this the sole claimant.
        var takeFocus = _focusService.FocusedPanel == WorkspacePanelId.None;

        if (EditorId.TryParse(tag, out var utilityId)
            && _contentControls.ContainsKey(utilityId)
            && !_dockedUtilityResources.ContainsKey(utilityId))
        {
            ShowSurface(utilityId, takeFocus);
        }
        else
        {
            // The persisted id no longer resolves: an uninstalled or disabled utility, an unexpected value, or a
            // utility that was docked during document restore (its WebView now lives in a document tab, so it
            // cannot be shown as a panel surface). Fall back to Explorer.
            ShowSurface(BuiltInUtilityIds.Explorer, takeFocus);
        }

        _selectionPersistenceEnabled = true;
    }

    private void PersistSelectedUtility(string tag)
    {
        if (!_selectionPersistenceEnabled)
        {
            return;
        }

        _settings.Set(SettingCatalog.Layout.UtilityPanelSelectedUtility, tag);
    }

    private bool IsCustomSurfaceSelected()
    {
        return IsCustomUtility(ViewModel.SelectedUtilityId);
    }

    private static bool IsCustomUtility(EditorId utilityId)
    {
        return !utilityId.IsEmpty
            && utilityId != BuiltInUtilityIds.Explorer
            && utilityId != BuiltInUtilityIds.Search
            && utilityId != BuiltInUtilityIds.ProjectSettings;
    }

    // Spotlight landmark id for a custom utility's rail button: its utility id followed by "-utility-button".
    // This must match the AutomationId set on the button.
    private static string CustomLandmarkId(EditorId utilityId)
    {
        return $"{utilityId}-utility-button";
    }

    private List<EditorId> GetCustomUtilityIds()
    {
        var customUtilityIds = new List<EditorId>();
        foreach (var utilityId in _contentControls.Keys)
        {
            if (IsCustomUtility(utilityId))
            {
                customUtilityIds.Add(utilityId);
            }
        }

        return customUtilityIds;
    }
}

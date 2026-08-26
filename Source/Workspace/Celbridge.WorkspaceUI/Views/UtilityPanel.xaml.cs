using Celbridge.Commands;
using Celbridge.Community;
using Celbridge.Documents;
using Celbridge.Explorer;
using Celbridge.Packages;
using Celbridge.Projects;
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
    private readonly IProjectService _projectService;
    private readonly ILayoutService _layoutService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    // The rail is hosted in a workspace column of its own, so it stays on screen while the panel is collapsed.
    // This panel still owns it: every button, its state and its click belong here.
    private readonly UtilityRail _rail;

    private readonly UtilityButton _explorerButton;
    private readonly UtilityButton _searchButton;
    private readonly UtilityButton _projectSettingsButton;

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

    // The utility a reveal selected while the panel was collapsed. A surface off screen cannot take the
    // keyboard, so the focus claim waits for the reveal to be laid out.
    private EditorId _pendingRevealUtilityId = EditorId.Empty;

    public IExplorerPanel ExplorerPanel { get; }
    public ISearchPanel SearchPanel { get; }

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

            return WorkspaceMinimumSize.ComposeSection(contentChrome).Width;
        }
    }

    /// <summary>
    /// The rail that selects this panel's surfaces, hosted beside the panel in the workspace layout.
    /// </summary>
    internal UtilityRail Rail => _rail;

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

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _focusService = ServiceLocator.AcquireService<IFocusService>();
        _settings = ServiceLocator.AcquireService<ISettingsService>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _spotlightRegistry = ServiceLocator.AcquireService<ISpotlightRegistry>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _projectService = ServiceLocator.AcquireService<IProjectService>();
        _layoutService = ServiceLocator.AcquireService<ILayoutService>();
        _workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();

        _rail = new UtilityRail();
        _explorerButton = new UtilityButton();
        _searchButton = new UtilityButton();
        _projectSettingsButton = new UtilityButton();

        // Acquire panel views via DI and host them in ContentControls
        ExplorerPanel = ServiceLocator.AcquireService<IExplorerPanel>();
        SearchPanel = ServiceLocator.AcquireService<ISearchPanel>();
        ExplorerPanelControl.Content = ExplorerPanel as UIElement;
        SearchPanelControl.Content = SearchPanel as UIElement;

        ViewModel = ServiceLocator.AcquireService<UtilityPanelViewModel>();
        ViewModel.SetPanelVisible(_layoutService.IsUtilityPanelVisible);
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

        _explorerButton.SetIcon(IconSymbol.Folder);
        _explorerButton.SetAutomationId(ExplorerLandmarkId);
        BindButton(_explorerButton, explorerItem);
        _explorerButton.Click += (sender, e) => ShowUtility(BuiltInUtilityIds.Explorer);
        _rail.AddUtilityButton(_explorerButton);

        _searchButton.SetIcon(IconSymbol.Search);
        _searchButton.SetAutomationId(SearchLandmarkId);
        BindButton(_searchButton, searchItem);
        _searchButton.Click += (sender, e) => ShowUtility(BuiltInUtilityIds.Search);
        _rail.AddUtilityButton(_searchButton);

        // Project Settings opens a document rather than selecting a surface, so the button is a launcher
        // like the community links: no rail item, no content host, no focus action.
        _projectSettingsButton.SetIcon(IconSymbol.Sliders);
        _projectSettingsButton.SetAutomationId(ProjectSettingsLandmarkId);
        _projectSettingsButton.Click += (sender, e) => OpenProjectSettings();
        _rail.AddLauncherButton(_projectSettingsButton);

        _buttons[BuiltInUtilityIds.Explorer] = _explorerButton;
        _buttons[BuiltInUtilityIds.Search] = _searchButton;
        _contentControls[BuiltInUtilityIds.Explorer] = ExplorerPanelControl;
        _contentControls[BuiltInUtilityIds.Search] = SearchPanelControl;
        _focusActions[BuiltInUtilityIds.Explorer] = ExplorerPanel.FocusPanel;
        _focusActions[BuiltInUtilityIds.Search] = SearchPanel.FocusSearchInput;
    }

    // Opens the Project Settings editor on the project file, naming the editor so the choice does not
    // depend on extension resolution. Already open, the command activates its tab.
    private void OpenProjectSettings()
    {
        var project = _projectService.CurrentProject;
        if (project is null)
        {
            return;
        }

        // The project file sits at the project root, so its resource key is just the file name.
        var projectFileName = Path.GetFileName(project.ProjectFilePath);
        if (!ResourceKey.TryCreate(projectFileName, out var projectFileResource))
        {
            return;
        }

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = projectFileResource;
            command.EditorId = BuiltInEditors.ProjectSettingsEditorId;
        });
    }

    private void InitializeCommunityButtons()
    {
        foreach (var link in CommunityLinks.All)
        {
            var railButton = new UtilityButton();
            railButton.SetIcon(link.Icon);
            railButton.SetAutomationId(link.LandmarkId);

            railButton.Click += (sender, e) => OpenCommunityLink(link);

            _rail.AddCommunityButton(railButton);

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
    }

    private void UtilityPanel_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTooltips();

        // Register how the hosted panels take keyboard focus, so the focus service can return focus to
        // whichever is focused after a modal dialog closes or the resource tree rebuilds.
        _focusService.SetPanelFocusHandler(WorkspacePanelId.Explorer, ExplorerPanel.FocusPanel);
        _focusService.SetPanelFocusHandler(WorkspacePanelId.Search, SearchPanel.FocusSearchInput);
        _focusService.SetPanelFocusHandler(WorkspacePanelId.CustomUtility, FocusActiveCustomUtility);

        // The utility panels drop their own header focus indicator and show focus on the selected rail button
        // instead, so feed panel focus changes into the view model to colour the indicator accordingly.
        _messengerService.Register<PanelFocusChangedMessage>(this, OnPanelFocusChanged);
        ViewModel.ReconcileFocus(_focusService.FocusedPanel);

        // Package discovery is what produces contribution issues, so the rail pip refreshes whenever a
        // discovery pass completes.
        _messengerService.Register<PackagesInitializedMessage>(this, OnPackagesInitialized);
        UpdateProjectSettingsIssuePip();

        // The rail marks follow the panel's visibility. The workspace restores that visibility around this
        // point, so it is read again here.
        _messengerService.Register<SurfaceVisibilityChangedMessage>(this, OnSurfaceVisibilityChanged);
        ViewModel.SetPanelVisible(_layoutService.IsUtilityPanelVisible);
    }

    private void UtilityPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        _messengerService.Unregister<PanelFocusChangedMessage>(this);
        _messengerService.Unregister<PackagesInitializedMessage>(this);
        _messengerService.Unregister<SurfaceVisibilityChangedMessage>(this);
        _focusService.SetPanelFocusHandler(WorkspacePanelId.Explorer, null);
        _focusService.SetPanelFocusHandler(WorkspacePanelId.Search, null);
        _focusService.SetPanelFocusHandler(WorkspacePanelId.CustomUtility, null);
    }

    // The CustomUtility panel is whichever contributed utility the rail has selected, so the handler resolves
    // it when it is called rather than one being registered per utility.
    private void FocusActiveCustomUtility()
    {
        if (_focusActions.TryGetValue(ActiveUtilityId, out var focusContent))
        {
            focusContent();
        }
    }

    private void OnPackagesInitialized(object recipient, PackagesInitializedMessage message)
    {
        UpdateProjectSettingsIssuePip();
    }

    // Flags the Project Settings rail button when any contribution has dropped configuration.
    private void UpdateProjectSettingsIssuePip()
    {
        var packageService = _workspaceWrapper.WorkspaceService?.PackageService;
        var hasIssues = packageService is not null
            && packageService.GetContributionIssues().Count > 0;

        _projectSettingsButton.SetIssuePipVisible(hasIssues);
    }

    private void ApplyTooltips()
    {
        var explorerTooltip = _stringLocalizer.GetString("UtilityPanel_ExplorerTooltip");
        _explorerButton.SetTooltip(explorerTooltip);

        var searchTooltip = _stringLocalizer.GetString("UtilityPanel_SearchTooltip");
        _searchButton.SetTooltip(searchTooltip);

        var projectSettingsTooltip = _stringLocalizer.GetString("UtilityPanel_ProjectSettingsTooltip");
        _projectSettingsButton.SetTooltip(projectSettingsTooltip);

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

        // A lazy-load utility creates its WebView on first show. The surface is shown
        // immediately; the WebView attaches to it when initialization completes.
        _ = _workspaceWrapper.WorkspaceService.UtilityService.EnsureUtilityInitializedAsync(utilityId);

        // Showing a utility presents it, so a collapsed panel is brought back first. Its focus claim waits
        // for the reveal to be laid out.
        bool isPanelVisible = _layoutService.IsUtilityPanelVisible;
        if (!isPanelVisible)
        {
            _pendingRevealUtilityId = utilityId;
            ShowUtilityPanel();
        }

        ShowSurface(utilityId, takeFocus: isPanelVisible);
        PersistSelectedUtility(utilityId.ToString());
    }

    private void ShowUtilityPanel()
    {
        _commandService.Execute<ISetSurfaceVisibilityCommand>(command =>
        {
            command.Surfaces = WorkspaceSurface.UtilityPanel;
            command.IsVisible = true;
        });
    }

    private void OnSurfaceVisibilityChanged(object recipient, SurfaceVisibilityChangedMessage message)
    {
        bool isPanelVisible = message.SurfaceVisibility.HasFlag(WorkspaceSurface.UtilityPanel);

        // A collapsed panel is showing nothing, so no rail item is marked. The selection is kept, so a reveal
        // returns to it.
        ViewModel.SetPanelVisible(isPanelVisible);

        // Every surface reports through this message, so one about the Bottom or Side area says nothing about
        // a reveal still waiting on the panel.
        if (!isPanelVisible)
        {
            return;
        }

        var revealedUtilityId = _pendingRevealUtilityId;
        _pendingRevealUtilityId = EditorId.Empty;

        if (revealedUtilityId.IsEmpty)
        {
            return;
        }

        // Deferred to a low dispatcher tick so the reveal has been laid out before the surface claims the
        // keyboard. A later selection supersedes this one, so the id is checked again on the tick.
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                if (ViewModel.SelectedUtilityId == revealedUtilityId)
                {
                    ShowSurface(revealedUtilityId);
                }
            });
    }

    // Makes the utility the one the panel shows and shows its content. takeFocus carries the keyboard to it,
    // and with it the optimistic accent that holds until focus settles.
    private void ShowSurface(EditorId utilityId, bool takeFocus = true)
    {
        if (!_contentControls.TryGetValue(utilityId, out var content))
        {
            return;
        }

        ViewModel.SelectUtility(utilityId, awaitFocus: takeFocus);
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

            _rail.AddUtilityButton(railButton);

            _spotlightRegistry.RegisterLandmark(new LandmarkDescriptor(landmarkId, null));

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
            _rail.RemoveUtilityButton(railButton);

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

        // A utility that has left for a document tab can no longer be the panel's surface, so the panel falls
        // back to Explorer. The keyboard belongs to the tab the utility just moved into.
        if (isDocument
            && ViewModel.SelectedUtilityId == utilityId)
        {
            ShowSurface(BuiltInUtilityIds.Explorer, takeFocus: false);
            PersistSelectedUtility(BuiltInUtilityIds.Explorer.ToString());
        }
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
            && utilityId != BuiltInUtilityIds.Search;
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

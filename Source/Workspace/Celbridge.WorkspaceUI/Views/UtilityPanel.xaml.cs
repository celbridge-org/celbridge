using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Explorer;
using Celbridge.Packages;
using Celbridge.Search;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.WorkspaceUI.ViewModels;
using Celbridge.WorkspaceUI.Views.Controls;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Media.Animation;

namespace Celbridge.WorkspaceUI.Views;

public sealed partial class UtilityPanel : UserControl, IUtilityPanel
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IFocusService _focusService;
    private readonly ISettingsService _settings;
    private readonly IMessengerService _messengerService;
    private readonly ISpotlightRegistry _spotlightRegistry;
    private readonly ICommandService _commandService;
    private readonly ILayoutService _layoutService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IIconService _iconService;

    // The rail is hosted in a workspace column of its own, so it stays on screen while the panel is collapsed.
    // This panel still owns it: every button, its state and its click belong here.
    private readonly UtilityRail _rail;

    // Spotlight landmark ids for the built-in rail buttons. These must match the descriptors seeded in
    // SpotlightLandmarks exactly.
    private const string ExplorerLandmarkId = "explorer-utility-button";
    private const string SearchLandmarkId = "search-utility-button";

    // Explorer and Search work in a portrait column and nowhere else.
    private static readonly IReadOnlyList<WorkspaceArea> PanelOnlyAreas =
    [
        WorkspaceArea.Utility
    ];

    // Rail buttons, content hosts, and focus callbacks for every surface (built-in and custom), keyed by
    // utility id. The view owns content hosting and focus acquisition. The view model owns the rail selection
    // and focus state, which the buttons bind to.
    private readonly Dictionary<EditorId, UtilityButton> _buttons = new();
    private readonly Dictionary<EditorId, ContentControl> _contentControls = new();
    private readonly Dictionary<EditorId, Action> _focusActions = new();

    // Docked utilities (utility id -> the document resource its WebView is docked into). A docked utility's rail
    // click activates its document tab instead of showing the panel surface.
    private readonly Dictionary<EditorId, ResourceKey> _dockedUtilityResources = new();

    // Launchers (rail id -> the document it opens). A launcher never occupies the panel, so this is what its
    // button click and its reveal both go through.
    private readonly Dictionary<EditorId, UtilityRailResource> _launcherResources = new();

    // The built-in utility descriptors this panel builds for itself, published to the utility service so the
    // rail register holds every item rather than only the ones the service builds.
    private readonly List<UtilityRailItem> _builtInUtilityItems = new();

    // The rail's buttons in the three ordered groups the panel presents: the built-in surfaces, then the
    // contribution utilities, then the launchers. The rail draws them as one stack, so the panel rebuilds it
    // from these whenever the middle group changes.
    private readonly List<UtilityButton> _surfaceButtons = new();
    private readonly List<UtilityButton> _customUtilityButtons = new();
    private readonly List<UtilityButton> _launcherButtons = new();

    private Storyboard? _perimeterStoryboard;

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

        double edge = WorkspaceConstants.SectionEdgeThickness;
        ContentArea.BorderThickness = new Thickness(edge, edge, edge, isPresented ? edge : 0);
        ContentArea.CornerRadius = new CornerRadius(panelCornerRadius, panelCornerRadius, bottomRadius, bottomRadius);

        // The flash outline traces the same shape as the chrome, at its own heavier thickness.
        PerimeterOverlay.BorderThickness = AttentionFlash.ResolveOutline(ContentArea.BorderThickness);
        PerimeterOverlay.CornerRadius = ContentArea.CornerRadius;
    }

    /// <summary>
    /// Briefly pulses an accent outline around the panel's perimeter.
    /// </summary>
    internal void FlashPerimeter()
    {
        _perimeterStoryboard?.Stop();
        _perimeterStoryboard = AttentionFlash.Play(PerimeterOverlay, AttentionFlash.OutlinePeakOpacity);
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
        _layoutService = ServiceLocator.AcquireService<ILayoutService>();
        _workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();

        _rail = new UtilityRail();
        _iconService = ServiceLocator.AcquireService<IIconService>();

        // Acquire panel views via DI. Their content hosts are created by the rail item build below.
        ExplorerPanel = ServiceLocator.AcquireService<IExplorerPanel>();
        SearchPanel = ServiceLocator.AcquireService<ISearchPanel>();

        ViewModel = ServiceLocator.AcquireService<UtilityPanelViewModel>();
        ViewModel.SetPanelVisible(_layoutService.IsUtilityPanelVisible);
        DataContext = ViewModel;

        BuildBuiltInUtilityItems();
        RefreshRailButtons();

        // Show the Explorer surface by default
        ShowSurface(BuiltInUtilityIds.Explorer);

        Loaded += UtilityPanel_Loaded;
        Unloaded += UtilityPanel_Unloaded;
    }

    // The panel's own built-in utilities, Explorer and Search. These wrap live views acquired from DI, so the
    // panel builds them rather than the utility service, which does not exist yet when the panel is constructed.
    private void BuildBuiltInUtilityItems()
    {
        string explorerName = _stringLocalizer.GetString("UtilityPanel_ExplorerTooltip");
        string searchName = _stringLocalizer.GetString("UtilityPanel_SearchTooltip");

        var explorerItem = new UtilityRailItem
        {
            ItemId = BuiltInUtilityIds.Explorer,
            LandmarkId = ExplorerLandmarkId,
            IconName = _iconService.GetIconName(IconSymbol.Folder),
            DisplayName = explorerName,
            Tooltip = explorerName,
            AllowedAreas = PanelOnlyAreas,
            PanelView = new UtilityRailPanelView(
                ExplorerPanel,
                ExplorerPanel.FocusPanel,
                WorkspacePanelId.Explorer,
                PreservePanelFocus: true)
        };

        var searchItem = new UtilityRailItem
        {
            ItemId = BuiltInUtilityIds.Search,
            LandmarkId = SearchLandmarkId,
            IconName = _iconService.GetIconName(IconSymbol.Search),
            DisplayName = searchName,
            Tooltip = searchName,
            AllowedAreas = PanelOnlyAreas,
            PanelView = new UtilityRailPanelView(
                SearchPanel,
                SearchPanel.FocusSearchInput,
                WorkspacePanelId.Search,
                PreservePanelFocus: true)
        };

        _builtInUtilityItems.Add(explorerItem);
        _builtInUtilityItems.Add(searchItem);

        _surfaceButtons.Add(AddRailItem(explorerItem));
        _surfaceButtons.Add(AddRailItem(searchItem));
    }

    // Builds a rail button from a descriptor and registers everything the panel tracks for it. An item that
    // can occupy the panel gets a rail item, a content host and a focus action; an item that only opens a
    // document gets none of those, and its click opens that document instead.
    private UtilityButton AddRailItem(UtilityRailItem item)
    {
        var railButton = new UtilityButton();
        railButton.SetIcon(item.IconName);
        railButton.SetAutomationId(item.LandmarkId);
        railButton.SetTooltip(item.Tooltip);

        var itemId = item.ItemId;
        var panelView = item.PanelView;
        var resource = item.Resource;

        if (panelView is not null)
        {
            var viewModelItem = ViewModel.AddItem(itemId, panelView.FocusIdentity);
            BindButton(railButton, viewModelItem);
            railButton.Click += (sender, e) => ShowUtility(itemId);

            _contentControls[itemId] = CreateContentHost(panelView);
            _focusActions[itemId] = panelView.FocusPanel;
        }
        else if (resource is not null)
        {
            _launcherResources[itemId] = resource;
            railButton.Click += (sender, e) => ShowLauncherDocument(itemId, resource);
        }

        _buttons[itemId] = railButton;

        return railButton;
    }

    // Hosts a rail item's panel view. The host sits above the hosted view's own FocusTracking.Panel
    // declaration: focusing the view lands managed focus here, and the focus tracker classifies by walking
    // towards the root, so without a declaration here the walk would pass the view's own and report None.
    private ContentControl CreateContentHost(UtilityRailPanelView panelView)
    {
        var contentControl = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
            Content = panelView.Content as UIElement
        };

        FocusTracking.SetPanel(contentControl, panelView.FocusIdentity);

        // A tree rebuild destroys the focused row and the platform bounces focus up onto the host, which has
        // no panel ancestor and would otherwise clear panel focus to None.
        if (panelView.PreservePanelFocus)
        {
            FocusTracking.SetPreservePanelFocus(contentControl, true);
        }

        ContentArea.Children.Add(contentControl);

        return contentControl;
    }

    // Opens a launcher's document. Already open, the command activates its tab.
    private void ShowLauncherDocument(EditorId itemId, UtilityRailResource resource)
    {
        FlashRailButton(itemId);

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resource.Resource;
            command.EditorId = resource.Editor;
        });
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
        if (!_buttons.TryGetValue(BuiltInLauncherIds.ProjectSettings, out var projectSettingsButton))
        {
            return;
        }

        var packageService = _workspaceWrapper.WorkspaceService?.PackageService;
        var hasIssues = packageService is not null
            && packageService.GetContributionIssues().Count > 0;

        projectSettingsButton.SetIssuePipVisible(hasIssues);
    }

    private void OnPanelFocusChanged(object recipient, PanelFocusChangedMessage message)
    {
        ViewModel.ReconcileFocus(message.FocusedPanel);
    }

    public bool HasRailItem(EditorId itemId)
    {
        return _buttons.ContainsKey(itemId);
    }

    public void ShowUtility(EditorId utilityId)
    {
        // A launcher has no panel surface, so revealing it is opening its document, the same as clicking it.
        if (_launcherResources.TryGetValue(utilityId, out var launcherResource))
        {
            ShowLauncherDocument(utilityId, launcherResource);
            return;
        }

        // A utility docked as a document activates its document tab, without changing the shown panel surface or
        // the rail highlight. A utility in the panel selects its rail surface.
        if (_dockedUtilityResources.TryGetValue(utilityId, out var documentResource))
        {
            // Activate the docked utility's tab, then request an attention flash so the reveal gives visible
            // feedback even when the tab was already the active document.
            FlashRailButton(utilityId);
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

    private void FlashRailButton(EditorId utilityId)
    {
        if (_buttons.TryGetValue(utilityId, out var railButton))
        {
            railButton.FlashAttention();
        }
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

        // Below the perimeter flash overlay, which takes the z-index above this one.
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

    public IReadOnlyList<UtilityRailItem> GetBuiltInUtilityItems()
    {
        return _builtInUtilityItems;
    }

    public void BuildRailItems(IReadOnlyList<UtilityRailItem> railItems)
    {
        ClearRailItems();

        foreach (var railItem in railItems)
        {
            // The rail surfaces are built in the constructor because they wrap live views, so they are already
            // on the rail. Rebuilding one would reparent the view it hosts.
            if (_buttons.ContainsKey(railItem.ItemId))
            {
                continue;
            }

            var railButton = AddRailItem(railItem);

            // An item with no panel view has nowhere to park a live view, so it is a launcher: it opens a
            // document, and joins the group the rail draws after the gap. A contribution's landmark is
            // registered here because it exists only while its package is loaded.
            if (railItem.PanelView is null)
            {
                _launcherButtons.Add(railButton);
            }
            else
            {
                _customUtilityButtons.Add(railButton);
                _spotlightRegistry.RegisterLandmark(new LandmarkDescriptor(railItem.LandmarkId, null));
            }
        }

        RefreshRailButtons();

        // The Project Settings button is rebuilt above, so its issue pip is re-applied to the new button.
        UpdateProjectSettingsIssuePip();
    }

    // Rebuilds the rail as two visual groups: the panel surfaces with the contribution utilities, then the
    // launchers. The rail draws the gap at the group boundary, so no button carries layout of its own.
    private void RefreshRailButtons()
    {
        _rail.ClearButtons();

        var utilityGroup = new List<UtilityButton>();
        utilityGroup.AddRange(_surfaceButtons);
        utilityGroup.AddRange(_customUtilityButtons);

        _rail.AddButtonGroup(utilityGroup);
        _rail.AddButtonGroup(_launcherButtons);
    }

    public void ClearRailItems()
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
            _customUtilityButtons.Remove(railButton);

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

        // The launchers host no live view and no content, so they are simply dropped and rebuilt. Their
        // landmarks are seeded at startup rather than registered per workspace, so none is unregistered here.
        foreach (var launcherId in _launcherResources.Keys)
        {
            _buttons.Remove(launcherId);
        }

        _launcherResources.Clear();
        _launcherButtons.Clear();

        RefreshRailButtons();
    }

    public void SetUtilityArea(EditorId utilityId, WorkspaceArea area, ResourceKey documentResource)
    {
        bool isDocument = area != WorkspaceArea.Utility;
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

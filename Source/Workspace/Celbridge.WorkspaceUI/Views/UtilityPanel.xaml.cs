using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Explorer;
using Celbridge.Packages;
using Celbridge.Search;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.Utilities;
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

    // Spotlight landmark ids for the built-in rail buttons. Each is set as its button's AutomationId,
    // which is what a landmark has to match.
    private const string ExplorerLandmarkId = "explorer-utility-button";
    private const string SearchLandmarkId = "search-utility-button";

    // Rail buttons, content hosts, and focus callbacks for every utility (built-in and custom), keyed by
    // utility id. The view owns content hosting and focus acquisition. The view model owns the rail selection
    // and focus state, which the buttons bind to.
    private readonly Dictionary<EditorId, UtilityButton> _buttons = new();
    private readonly Dictionary<EditorId, ContentControl> _contentControls = new();
    private readonly Dictionary<EditorId, Action> _focusActions = new();

    // Docked utilities (utility id -> the document resource its WebView is docked into). A docked utility's rail
    // click activates its document tab instead of showing it in the panel.
    private readonly Dictionary<EditorId, ResourceKey> _dockedUtilityResources = new();

    // The document shortcuts, by rail id. A shortcut never occupies the panel, so its descriptor is what
    // its button click and its reveal both go through.
    private readonly Dictionary<EditorId, UtilityRailItem> _shortcutItems = new();

    // The Spotlight landmark each rail button owns, by rail id. Unregistering reads the id that was
    // registered rather than rebuilding it, so the two cannot drift apart.
    private readonly Dictionary<EditorId, string> _landmarkIds = new();

    // The built-in utility descriptors this panel builds for itself, published to the utility service so the
    // rail register holds every item.
    private readonly List<UtilityRailItem> _builtInUtilityItems = new();

    // The rail's buttons in the three ordered groups the panel presents: the built-in utilities, then the
    // contribution utilities, then the document shortcuts. The rail draws them as one stack, so the panel
    // rebuilds it from these whenever the middle group changes.
    private readonly List<UtilityButton> _builtInUtilityButtons = new();
    private readonly List<UtilityButton> _customUtilityButtons = new();
    private readonly List<UtilityButton> _shortcutButtons = new();

    private Storyboard? _perimeterStoryboard;

    // Selection is persisted only after RestoreSelectedUtility runs, so the constructor's default selection and
    // the restore itself do not overwrite the saved selection before it is read.
    private bool _selectionPersistenceEnabled;

    // The utility a reveal selected while the panel was collapsed. A utility off screen cannot take the
    // keyboard, so the focus claim waits for the reveal to be laid out.
    private EditorId _pendingRevealUtilityId = EditorId.Empty;

    public IExplorerPanel ExplorerPanel { get; }
    public ISearchPanel SearchPanel { get; }

    public UtilityPanelViewModel ViewModel { get; }

    public EditorId ActiveUtilityId => ViewModel.SelectedUtilityId;

    /// <summary>
    /// The rail that selects the utility this panel shows, hosted beside the panel in the workspace layout.
    /// </summary>
    internal UtilityRail Rail => _rail;

    // Whether the panel draws a bottom edge and rounded bottom corners: it does when the Bottom document
    // area runs underneath it, and meets the application border flush otherwise. Driven by the layout
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
        ViewModel.SetPanelVisible(_layoutService.IsAreaVisible(WorkspaceArea.Utility));
        DataContext = ViewModel;

        BuildBuiltInUtilityItems();
        RefreshRailButtons();

        // Show the Explorer by default
        ShowUtilityInPanel(BuiltInUtilityIds.Explorer);

        Loaded += UtilityPanel_Loaded;
        Unloaded += UtilityPanel_Unloaded;
    }

    // The panel's own built-in utilities, Explorer and Search. These wrap live views acquired from DI, so the
    // panel builds them rather than the utility service, which does not exist yet when the panel is constructed.
    private void BuildBuiltInUtilityItems()
    {
        var explorerName = _stringLocalizer.GetString("UtilityPanel_ExplorerTooltip");

        var explorerView = new UtilityRailPanelView(
            ExplorerPanel,
            ExplorerPanel.FocusPanel,
            FocusPanelId.Explorer,
            PreservePanelFocus: true);

        var explorerItem = UtilityRailItem.CreatePanelUtility(
            BuiltInUtilityIds.Explorer,
            ExplorerLandmarkId,
            _iconService.GetIconName(IconSymbol.Folder),
            explorerName,
            explorerName,
            explorerView);

        var searchName = _stringLocalizer.GetString("UtilityPanel_SearchTooltip");

        var searchView = new UtilityRailPanelView(
            SearchPanel,
            SearchPanel.FocusSearchInput,
            FocusPanelId.Search,
            PreservePanelFocus: true);

        var searchItem = UtilityRailItem.CreatePanelUtility(
            BuiltInUtilityIds.Search,
            SearchLandmarkId,
            _iconService.GetIconName(IconSymbol.Search),
            searchName,
            searchName,
            searchView);

        _builtInUtilityItems.Add(explorerItem);
        _builtInUtilityItems.Add(searchItem);

        _builtInUtilityButtons.Add(AddRailItem(explorerItem));
        _builtInUtilityButtons.Add(AddRailItem(searchItem));
    }

    // Builds a rail button and registers everything the panel tracks for it. An item that can occupy the
    // panel gets a view model item, a content host and a focus action. An item that only opens a document
    // gets none of those, and its click opens that document instead.
    private UtilityButton AddRailItem(UtilityRailItem item)
    {
        var railButton = new UtilityButton();
        railButton.SetIcon(item.IconName);
        railButton.SetAutomationId(item.LandmarkId);
        railButton.SetTooltip(item.Tooltip);

        var itemId = item.ItemId;

        switch (item.Kind)
        {
            case RailItemKind.PanelUtility:
            case RailItemKind.DockableUtility:
                Guard.IsNotNull(item.PanelView);
                var panelView = item.PanelView;

                var viewModelItem = ViewModel.AddItem(itemId, panelView.FocusIdentity);
                BindButton(railButton, viewModelItem);
                railButton.Click += (sender, e) => ShowUtility(itemId);

                _contentControls[itemId] = CreateContentHost(panelView);
                _focusActions[itemId] = panelView.FocusPanel;
                break;

            case RailItemKind.DocumentShortcut:
                _shortcutItems[itemId] = item;
                railButton.Click += (sender, e) => ShowShortcutDocument(item);
                break;

            default:
                throw new NotSupportedException($"Unhandled rail item kind '{item.Kind}'");
        }

        // A landmark lives exactly as long as the button it points at. The built-in utility buttons last
        // for the life of the panel, and the rest are dropped when the rail is cleared.
        _landmarkIds[itemId] = item.LandmarkId;

        var landmark = new LandmarkDescriptor(item.LandmarkId, null);
        _spotlightRegistry.RegisterLandmark(landmark);

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

    // Opens a shortcut's document in the area it declares. Already open, the command activates its tab.
    private void ShowShortcutDocument(UtilityRailItem item)
    {
        FlashRailButton(item.ItemId);

        // A document shortcut always names the area its document opens in.
        var area = item.DockArea!.Value;

        // Opening into a section does not reveal its area, so a collapsed one is presented first.
        PresentArea(area);

        // The declared area decides where the document lands when it opens. A tab that is already open keeps
        // the section the user put it in, which is what an unnamed section means to the open command.
        var targetSection = ResolveShortcutSection(item.FileResource, area);

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = item.FileResource;
            command.EditorId = item.EditorId;
            command.TargetSection = targetSection;
        });
    }

    // Null once the document is open, so the open command leaves the tab where the user put it.
    private DocumentSection? ResolveShortcutSection(ResourceKey resource, WorkspaceArea area)
    {
        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;
        if (documentsService.FindOpenDocument(resource) is not null)
        {
            return null;
        }

        return area.GetPrimaryDocumentSection();
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
        _focusService.SetPanelFocusHandler(FocusPanelId.Explorer, ExplorerPanel.FocusPanel);
        _focusService.SetPanelFocusHandler(FocusPanelId.Search, SearchPanel.FocusSearchInput);
        _focusService.SetPanelFocusHandler(FocusPanelId.CustomUtility, FocusActiveCustomUtility);

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
        _messengerService.Register<AreaVisibilityChangedMessage>(this, OnAreaVisibilityChanged);
        ViewModel.SetPanelVisible(_layoutService.IsAreaVisible(WorkspaceArea.Utility));
    }

    private void UtilityPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        _messengerService.Unregister<PanelFocusChangedMessage>(this);
        _messengerService.Unregister<PackagesInitializedMessage>(this);
        _messengerService.Unregister<AreaVisibilityChangedMessage>(this);
        _focusService.SetPanelFocusHandler(FocusPanelId.Explorer, null);
        _focusService.SetPanelFocusHandler(FocusPanelId.Search, null);
        _focusService.SetPanelFocusHandler(FocusPanelId.CustomUtility, null);
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
        if (!_buttons.TryGetValue(BuiltInShortcutIds.ProjectSettings, out var projectSettingsButton))
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
        // A document shortcut never occupies the panel, so revealing it is opening its document, the same
        // as clicking it.
        if (_shortcutItems.TryGetValue(utilityId, out var shortcutItem))
        {
            ShowShortcutDocument(shortcutItem);
            return;
        }

        // A utility docked as a document activates its document tab, without changing which utility the panel
        // shows or the rail highlight. A utility in the panel is selected on the rail.
        if (_dockedUtilityResources.TryGetValue(utilityId, out var documentResource))
        {
            // Reveal the area holding the tab before activating it, so a utility docked in a collapsed area
            // is actually presented rather than activated out of sight.
            var dockedArea = _workspaceWrapper.WorkspaceService.UtilityService.GetCurrentArea(utilityId);
            if (dockedArea is not null)
            {
                PresentArea(dockedArea.Value);
            }

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

        // Showing a utility presents it, so a collapsed panel is brought back first. Its focus claim waits
        // for the reveal to be laid out.
        bool isPanelVisible = _layoutService.IsAreaVisible(WorkspaceArea.Utility);
        if (!isPanelVisible)
        {
            _pendingRevealUtilityId = utilityId;
            ShowUtilityPanel();
        }

        ShowUtilityInPanel(utilityId, takeFocus: isPanelVisible);
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
        PresentArea(WorkspaceArea.Utility);
    }

    // Reveals a collapsed area, so presenting something in it puts it on screen. Main is never collapsed,
    // which is the no-op case.
    private void PresentArea(WorkspaceArea area)
    {
        if (!area.IsCollapsible())
        {
            return;
        }

        _commandService.Execute<ISetAreaVisibilityCommand>(command =>
        {
            command.Area = area;
            command.IsVisible = true;
        });
    }

    private void OnAreaVisibilityChanged(object recipient, AreaVisibilityChangedMessage message)
    {
        bool isPanelVisible = message.VisibleAreas.Contains(WorkspaceArea.Utility);

        // A collapsed panel is showing nothing, so no rail item is marked. The selection is kept, so a reveal
        // returns to it.
        ViewModel.SetPanelVisible(isPanelVisible);

        // Every area reports through this message, so one about the Bottom or Side area says nothing about
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

        // Deferred to a low dispatcher tick so the reveal has been laid out before the utility claims the
        // keyboard. A later selection supersedes this one, so the id is checked again on the tick.
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                if (ViewModel.SelectedUtilityId == revealedUtilityId)
                {
                    ShowUtilityInPanel(revealedUtilityId);
                }
            });
    }

    // Makes the utility the one the panel shows and shows its content. takeFocus carries the keyboard to it,
    // and with it the optimistic accent that holds until focus settles.
    private void ShowUtilityInPanel(EditorId utilityId, bool takeFocus = true)
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
    // content hosts. Keeping the outgoing content visible until focus has moved onto the incoming one stops
    // WinUI from relocating focus to another panel when the previously focused element would otherwise be
    // collapsed. Focusing after layout (rather than this tick) lands on a control that is actually focusable.
    private void ShowContent(EditorId utilityId, ContentControl content, bool takeFocus)
    {
        // Content that is already visible (re-selected while another panel holds focus, e.g. after a
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
            // outgoing content. Collapsing it below would then relocate managed focus onto
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

    // Broadcasts the now-active utility, so app-level state can report it without touching this UI object
    // off the UI thread.
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
        RemoveBuiltRailItems();

        foreach (var railItem in railItems)
        {
            // The built-in utilities are built in the constructor because they wrap live views, so they are already
            // on the rail. Rebuilding one would reparent the view it hosts.
            if (_buttons.ContainsKey(railItem.ItemId))
            {
                continue;
            }

            var railButton = AddRailItem(railItem);

            // A document shortcut joins the group the rail draws after the gap.
            if (railItem.Kind == RailItemKind.DocumentShortcut)
            {
                _shortcutButtons.Add(railButton);
            }
            else
            {
                _customUtilityButtons.Add(railButton);
            }
        }

        RefreshRailButtons();

        // The Project Settings button is rebuilt above, so its issue pip is re-applied to the new button.
        UpdateProjectSettingsIssuePip();
    }

    // Rebuilds the rail as two visual groups: the built-in utilities with the contribution utilities, then
    // the document shortcuts. The rail draws the gap at the group boundary, so no button carries layout of
    // its own.
    private void RefreshRailButtons()
    {
        _rail.ClearButtons();

        var utilityGroup = new List<UtilityButton>();
        utilityGroup.AddRange(_builtInUtilityButtons);
        utilityGroup.AddRange(_customUtilityButtons);

        _rail.AddButtonGroup(utilityGroup);
        _rail.AddButtonGroup(_shortcutButtons);
    }

    public void ClearRailItems()
    {
        RemoveBuiltRailItems();
        RefreshRailButtons();
    }

    // Drops every rail item the panel built, leaving the rail itself untouched, so a rebuild redraws it once
    // rather than once per phase.
    private void RemoveBuiltRailItems()
    {
        // This runs on unload and on rebuild, so the revert-to-Explorer below must not persist over the user's
        // saved selection. RestoreSelectedUtility re-enables persistence once the rebuilt rail is restored.
        _selectionPersistenceEnabled = false;

        // Revert to Explorer before removing items so a removed utility is never left showing or highlighted.
        if (IsCustomUtilitySelected())
        {
            ShowUtilityInPanel(BuiltInUtilityIds.Explorer);
        }

        foreach (var utilityId in GetCustomUtilityIds())
        {
            var railButton = _buttons[utilityId];
            _customUtilityButtons.Remove(railButton);

            var contentControl = _contentControls[utilityId];
            contentControl.Content = null;
            ContentArea.Children.Remove(contentControl);

            UnregisterRailLandmark(utilityId);

            _buttons.Remove(utilityId);
            _contentControls.Remove(utilityId);
            _focusActions.Remove(utilityId);
            _dockedUtilityResources.Remove(utilityId);
            ViewModel.RemoveItem(utilityId);
        }

        // The document shortcuts host no live view and no content, so they are simply dropped and rebuilt.
        foreach (var shortcutItem in _shortcutItems.Values)
        {
            _buttons.Remove(shortcutItem.ItemId);
            UnregisterRailLandmark(shortcutItem.ItemId);
        }

        _shortcutItems.Clear();
        _shortcutButtons.Clear();
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

        // A utility that has left for a document tab can no longer be what the panel shows, so the panel falls
        // back to Explorer. The keyboard belongs to the tab the utility just moved into.
        if (isDocument
            && ViewModel.SelectedUtilityId == utilityId)
        {
            ShowUtilityInPanel(BuiltInUtilityIds.Explorer, takeFocus: false);
            PersistSelectedUtility(BuiltInUtilityIds.Explorer.ToString());
        }
    }

    public void RestoreSelectedUtility()
    {
        var tag = _settings.Get(SettingCatalog.Layout.UtilityPanelSelectedUtility);

        // Restoring the previously selected utility shows it. It is not the user choosing to work
        // in that panel, so it claims the keyboard only as a fallback. The restored active document takes
        // focus first and keeps it, and a workspace with no open document leaves this the sole claimant.
        var takeFocus = _focusService.FocusedPanel == FocusPanelId.None;

        if (EditorId.TryParse(tag, out var utilityId)
            && _contentControls.ContainsKey(utilityId)
            && !_dockedUtilityResources.ContainsKey(utilityId))
        {
            ShowUtilityInPanel(utilityId, takeFocus);
        }
        else
        {
            // The persisted id no longer resolves: an uninstalled or disabled utility, an unexpected value, or a
            // utility that was docked during document restore (its WebView now lives in a document tab, so it
            // cannot be shown in the panel). Fall back to Explorer.
            ShowUtilityInPanel(BuiltInUtilityIds.Explorer, takeFocus);
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

    private bool IsCustomUtilitySelected()
    {
        return IsCustomUtility(ViewModel.SelectedUtilityId);
    }

    private static bool IsCustomUtility(EditorId utilityId)
    {
        return !utilityId.IsEmpty
            && utilityId != BuiltInUtilityIds.Explorer
            && utilityId != BuiltInUtilityIds.Search;
    }

    // Drops the landmark registered for a rail button, by the id the button was registered under.
    private void UnregisterRailLandmark(EditorId itemId)
    {
        if (!_landmarkIds.Remove(itemId, out var landmarkId))
        {
            return;
        }

        _spotlightRegistry.UnregisterLandmark(landmarkId);
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

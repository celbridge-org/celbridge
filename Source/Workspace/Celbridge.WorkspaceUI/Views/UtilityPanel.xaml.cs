using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Search;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.WorkspaceUI.ViewModels;
using Celbridge.WorkspaceUI.Views.Controls;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Data;

namespace Celbridge.WorkspaceUI.Views;

public sealed partial class UtilityPanel : UserControl, IUtilityPanel
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IFocusService _focusService;
    private readonly ISettingsService _settings;
    private readonly IMessengerService _messengerService;
    private readonly ISpotlightRegistry _spotlightRegistry;
    private readonly ICommandService _commandService;

    // Spotlight landmark ids for the built-in rail buttons. Seeded as descriptors in SpotlightLandmarks and
    // documented in the app_spotlight guide, so they must match those exactly.
    private const string ExplorerLandmarkId = "explorer-utility-button";
    private const string SearchLandmarkId = "search-utility-button";

    // Rail buttons, content hosts, and focus callbacks for every surface (built-in and custom), keyed by
    // utility id. The view owns content hosting and focus acquisition; the view model owns the rail selection
    // and focus state, which the buttons bind to.
    private readonly Dictionary<EditorInstanceId, UtilityButton> _buttons = new();
    private readonly Dictionary<EditorInstanceId, ContentControl> _contentControls = new();
    private readonly Dictionary<EditorInstanceId, Action> _focusActions = new();

    // Docked utilities (utility id -> the document resource its WebView is docked into). A docked utility's rail
    // click activates its document tab instead of showing the panel surface.
    private readonly Dictionary<EditorInstanceId, ResourceKey> _dockedUtilityResources = new();

    // Selection is persisted only after RestoreSelectedUtility runs, so the constructor's default selection and
    // the restore itself do not overwrite the saved selection before it is read.
    private bool _selectionPersistenceEnabled;

    public IExplorerPanel ExplorerPanel { get; }
    public ISearchPanel SearchPanel { get; }

    public UtilityPanelViewModel ViewModel { get; }

    public EditorInstanceId ActiveUtilityId => ViewModel.SelectedUtilityId;

    public UtilityPanel()
    {
        this.InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _focusService = ServiceLocator.AcquireService<IFocusService>();
        _settings = ServiceLocator.AcquireService<ISettingsService>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _spotlightRegistry = ServiceLocator.AcquireService<ISpotlightRegistry>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();

        // Acquire panel views via DI and host them in ContentControls
        ExplorerPanel = ServiceLocator.AcquireService<IExplorerPanel>();
        SearchPanel = ServiceLocator.AcquireService<ISearchPanel>();
        ExplorerPanelControl.Content = ExplorerPanel as UIElement;
        SearchPanelControl.Content = SearchPanel as UIElement;

        ViewModel = ServiceLocator.AcquireService<UtilityPanelViewModel>();
        DataContext = ViewModel;

        InitializeBuiltInButtons();

        // Show the Explorer surface by default
        ShowSurface(BuiltInUtilityIds.Explorer);

        Loaded += UtilityPanel_Loaded;
        Unloaded += UtilityPanel_Unloaded;
    }

    // Registers the two built-in rail items with the view model, configures their buttons (icon, spotlight
    // landmark id, click routing), binds them to their item view models, and records their content hosts and
    // focus callbacks. Tooltips are applied later in ApplyTooltips once the localizer strings are read.
    private void InitializeBuiltInButtons()
    {
        var explorerItem = ViewModel.AddItem(BuiltInUtilityIds.Explorer, WorkspacePanel.Explorer);
        var searchItem = ViewModel.AddItem(BuiltInUtilityIds.Search, WorkspacePanel.Search);

        ExplorerButton.SetIcon(IconSymbol.Folder);
        ExplorerButton.SetAutomationId(ExplorerLandmarkId);
        BindButton(ExplorerButton, explorerItem);
        ExplorerButton.Click += (sender, e) => ShowUtility(BuiltInUtilityIds.Explorer);

        SearchButton.SetIcon(IconSymbol.Search);
        SearchButton.SetAutomationId(SearchLandmarkId);
        BindButton(SearchButton, searchItem);
        SearchButton.Click += (sender, e) => ShowUtility(BuiltInUtilityIds.Search);

        _buttons[BuiltInUtilityIds.Explorer] = ExplorerButton;
        _buttons[BuiltInUtilityIds.Search] = SearchButton;
        _contentControls[BuiltInUtilityIds.Explorer] = ExplorerPanelControl;
        _contentControls[BuiltInUtilityIds.Search] = SearchPanelControl;
        _focusActions[BuiltInUtilityIds.Explorer] = ExplorerPanel.FocusPanel;
        _focusActions[BuiltInUtilityIds.Search] = SearchPanel.FocusSearchInput;
    }

    // Binds a rail button's visual state to its item view model, so selection and focus changes propagate
    // through data binding rather than imperative mutation.
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
        // whichever is focused after a modal dialog closes or the resource tree rebuilds. Only Explorer
        // and Search register a handler by design: the Documents and Console web surfaces and the
        // Inspector intentionally have none, so focus restore is a deliberate no-op for those and the
        // user re-focuses them with a single click.
        _focusService.SetPanelFocusHandler(WorkspacePanel.Explorer, ExplorerPanel.FocusPanel);
        _focusService.SetPanelFocusHandler(WorkspacePanel.Search, SearchPanel.FocusSearchInput);

        // The utility panels drop their own header focus indicator and show focus on the selected rail button
        // instead, so feed panel focus changes into the view model to colour the indicator accordingly.
        _messengerService.Register<PanelFocusChangedMessage>(this, OnPanelFocusChanged);
        ViewModel.ReconcileFocus(_focusService.FocusedPanel);
    }

    private void UtilityPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        _messengerService.Unregister<PanelFocusChangedMessage>(this);
        _focusService.SetPanelFocusHandler(WorkspacePanel.Explorer, null);
        _focusService.SetPanelFocusHandler(WorkspacePanel.Search, null);
    }

    private void ApplyTooltips()
    {
        var explorerTooltip = _stringLocalizer.GetString("UtilityPanel_ExplorerTooltip");
        ExplorerButton.SetTooltip(explorerTooltip);

        var searchTooltip = _stringLocalizer.GetString("UtilityPanel_SearchTooltip");
        SearchButton.SetTooltip(searchTooltip);
    }

    private void OnPanelFocusChanged(object recipient, PanelFocusChangedMessage message)
    {
        ViewModel.ReconcileFocus(message.FocusedPanel);
    }

    public void ShowUtility(EditorInstanceId utilityId)
    {
        // A utility docked as a document activates its document tab (without changing the shown panel surface or
        // the rail highlight); a utility in the panel selects its rail surface.
        if (_dockedUtilityResources.TryGetValue(utilityId, out var documentResource))
        {
            // Activate the docked utility's tab (a state change, so a command), then request an attention flash
            // so the reveal gives visible feedback even when the tab was already the active document. The flash
            // is a transient view effect, sent as a notification rather than run as a command.
            _commandService.Execute<IActivateDocumentCommand>(command => command.FileResource = documentResource);
            _messengerService.Send(new FlashDocumentMessage(documentResource));
            return;
        }

        if (!_contentControls.ContainsKey(utilityId))
        {
            return;
        }

        ShowSurface(utilityId);
        PersistSelectedUtility(utilityId.ToString());
    }

    // Selects the surface in the view model (which lights the accent optimistically) and shows its content.
    private void ShowSurface(EditorInstanceId utilityId)
    {
        if (!_contentControls.TryGetValue(utilityId, out var content))
        {
            return;
        }

        ViewModel.SelectUtility(utilityId);
        ShowContentWithFocus(utilityId, content);
        NotifyActiveUtilityChanged();
    }

    // Shows the incoming content on top and, once it has been laid out, focuses it and then collapses the other
    // content hosts. Keeping the outgoing content visible until focus has moved onto the incoming surface stops
    // WinUI from relocating focus to another panel when the previously focused element would otherwise be
    // collapsed. Focusing after layout (rather than this tick) lands on a control that is actually focusable.
    private void ShowContentWithFocus(EditorInstanceId utilityId, ContentControl content)
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
            FocusShownContent(utilityId, content);
            return;
        }

        void OnLayoutUpdated(object? sender, object args)
        {
            content.LayoutUpdated -= OnLayoutUpdated;
            FocusShownContent(utilityId, content);
        }

        content.LayoutUpdated += OnLayoutUpdated;
    }

    private void FocusShownContent(EditorInstanceId utilityId, ContentControl content)
    {
        // Drop a stale attempt when a later selection superseded this one before layout ran.
        if (ViewModel.SelectedUtilityId != utilityId
            || content.Visibility != Visibility.Visible)
        {
            return;
        }

        if (_focusActions.TryGetValue(utilityId, out var focusContent))
        {
            focusContent();
        }

        // A web-view utility's focusContent moves only native focus, so managed focus stays on the
        // outgoing surface. Collapsing that surface below would then relocate managed focus onto
        // unrelated chrome (a document tab), clobbering the web view's just-reported CustomUtility panel.
        // Park managed focus on this utility's host - it carries the CustomUtility panel declaration - so
        // the collapse has nothing to relocate. Use Pointer state to match the focus the host receives
        // naturally when switching in from a managed panel (which leaves web-view typing working);
        // Programmatic focus would instead route keys away from the web content on macOS.
        if (IsCustomUtility(utilityId))
        {
            content.Focus(FocusState.Pointer);
        }

        ViewModel.ReconcileFocus(_focusService.FocusedPanel);

        CollapseContentExcept(content);
    }

    // Collapses every content host except the one shown.
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

    // Broadcasts the now-active rail surface as a unified utility id, so app-level state (e.g. app_get_state)
    // can report it without touching this UI object off the UI thread.
    private void NotifyActiveUtilityChanged()
    {
        _messengerService.Send(new ActiveUtilityChangedMessage(ActiveUtilityId.ToString()));
    }

    public void BuildCustomUtilities(IReadOnlyList<CustomUtility> utilities)
    {
        ClearCustomUtilities();

        foreach (var utility in utilities)
        {
            var item = ViewModel.AddItem(utility.UtilityId, WorkspacePanel.CustomUtility);

            var railButton = new UtilityButton();
            railButton.SetIcon(utility.IconGlyphName);
            railButton.SetTooltip(utility.Tooltip);

            var landmarkId = CustomLandmarkId(utility.UtilityId);
            railButton.SetAutomationId(landmarkId);

            BindButton(railButton, item);

            var utilityId = utility.UtilityId;
            railButton.Click += (sender, e) => ShowUtility(utilityId);

            RailItems.Children.Add(railButton);

            _spotlightRegistry.RegisterLandmark(new LandmarkDescriptor(landmarkId, LayoutRegion.Primary));

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
            FocusTracking.SetPanel(contentControl, WorkspacePanel.CustomUtility);

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

    public void SetUtilityDockLocation(EditorInstanceId utilityId, DockLocation location, ResourceKey documentResource)
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

    public void FlashUtility(EditorInstanceId utilityId)
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

        if (EditorInstanceId.TryParse(tag, out var utilityId)
            && _contentControls.ContainsKey(utilityId)
            && !_dockedUtilityResources.ContainsKey(utilityId))
        {
            ShowSurface(utilityId);
        }
        else
        {
            // The persisted id no longer resolves: an uninstalled or disabled utility, an unexpected value, or a
            // utility that was docked during document restore (its WebView now lives in a document tab, so it
            // cannot be shown as a panel surface). Fall back to Explorer.
            ShowSurface(BuiltInUtilityIds.Explorer);
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

    private static bool IsCustomUtility(EditorInstanceId utilityId)
    {
        return !utilityId.IsEmpty
            && utilityId != BuiltInUtilityIds.Explorer
            && utilityId != BuiltInUtilityIds.Search;
    }

    // Spotlight landmark id for a custom utility's rail button: its utility id followed by "-utility-button",
    // matching the AutomationId set on the button and the app_spotlight guide so an agent can resolve it. The rail
    // is always visible when the Primary region is shown, so these landmarks need only a region reveal, matching
    // the built-in Explorer and Search rail buttons.
    private static string CustomLandmarkId(EditorInstanceId utilityId)
    {
        return $"{utilityId}-utility-button";
    }

    private List<EditorInstanceId> GetCustomUtilityIds()
    {
        var customUtilityIds = new List<EditorInstanceId>();
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

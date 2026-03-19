using System.ComponentModel;
using Celbridge.Inspector.ViewModels;
using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Inspector.Views;

public sealed partial class InspectorPanel : UserControl, IInspectorPanel
{
    private readonly ILogger<InspectorPanel> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IInspectorService _inspectorService;
    private readonly IPanelFocusService _panelFocusService;

    public InspectorPanelViewModel ViewModel { get; }

    private string TitleString => _stringLocalizer.GetString("InspectorPanel_Title");

    public InspectorPanel()
    {
        _logger = ServiceLocator.AcquireService<ILogger<InspectorPanel>>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _panelFocusService = ServiceLocator.AcquireService<IPanelFocusService>();

        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        _inspectorService = workspaceWrapper.WorkspaceService.InspectorService;

        ViewModel = ServiceLocator.AcquireService<InspectorPanelViewModel>();

        InitializeComponent();

        DataContext = ViewModel;

        Loaded += (s, e) =>
        {
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        };

        Unloaded += (s, e) =>
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        };
    }

    private void UserControl_GotFocus(object sender, RoutedEventArgs e)
    {
        _panelFocusService.SetFocusedPanel(WorkspacePanel.Inspector);
    }

    private void UserControl_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _panelFocusService.SetFocusedPanel(WorkspacePanel.Inspector);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.SelectedResource))
        {
            UpdateSelectedResource(ViewModel.SelectedResource);
        }
    }

    private void UpdateSelectedResource(ResourceKey resource)
    {
        EntityEditor.ClearComponentListPanel();

        if (resource.IsEmpty)
        {
            return;
        }

        var factory = _inspectorService.InspectorFactory;

        var inspectorElements = new List<UIElement>();

        // Resource name inspector (top of panel)
        var nameInspectorResult = factory.CreateResourceNameInspector(resource);
        if (nameInspectorResult.IsFailure)
        {
            _logger.LogError(nameInspectorResult, $"Failed to create resource name inspector for resource: {resource}");
            return;
        }
        var nameInspector = nameInspectorResult.Value as UserControl;
        Guard.IsNotNull(nameInspector);
        inspectorElements.Add(nameInspector);

        // Optional resource inspector
        var resourceInspectorResult = factory.CreateResourceInspector(resource);
        if (resourceInspectorResult.IsSuccess)
        {
            var resourceInspector = resourceInspectorResult.Value as UserControl;
            Guard.IsNotNull(resourceInspector);
            inspectorElements.Add(resourceInspector);
        }
        else
        {
            // Only show component list view if no resource-specific inspector is available
            var componentListResult = factory.CreateComponentListView(resource);
            if (componentListResult.IsSuccess)
            {
                var entityInspector = componentListResult.Value as UserControl;
                Guard.IsNotNull(entityInspector);
                inspectorElements.Add(entityInspector);
            }
        }

        EntityEditor.PopulateComponentsPanel(inspectorElements);
    }
}

using Celbridge.Inspector.ViewModels;
using Celbridge.Logging;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Inspector.Views;

public sealed partial class InspectorPanel : UserControl, IInspectorPanel
{
    private readonly IInspectorService _inspectorService;
    public InspectorPanelViewModel ViewModel { get; }

    public LocalizedString TitleString => _stringLocalizer.GetString($"InspectorPanel_Title");

    private ILogger<InspectorPanel> _logger;
    private IStringLocalizer _stringLocalizer;

    private EntityEditor _entityEditor;

    public InspectorPanel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        _logger = serviceProvider.GetRequiredService<ILogger<InspectorPanel>>();
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        var workspaceWrapper = serviceProvider.GetRequiredService<IWorkspaceWrapper>();
        _inspectorService = workspaceWrapper.WorkspaceService.InspectorService;

        ViewModel = serviceProvider.GetRequiredService<InspectorPanelViewModel>();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        var titleBar = new Grid()
            .Background(ThemeResource.Get<Brush>("PanelBackgroundBrush"))
            .BorderBrush(ThemeResource.Get<Brush>("PanelBorderBrush"))
            .BorderThickness(0, 0, 0, 1)
            .ColumnDefinitions("Auto, *")
            .Children(
                new TextBlock()
                    .Grid(column: 0)
                    .Text(TitleString)
                    .Margin(6, 0, 0, 0)
                    .VerticalAlignment(VerticalAlignment.Center)
            );

        _entityEditor = new EntityEditor()
            .Margin(4)
            .Grid(row: 1);

        var panelGrid = new Grid()
            .RowDefinitions("40, *")
            .Children(titleBar, _entityEditor);
           
        //
        // Set the data context and page content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(panelGrid));
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.SelectedResource))
        {
            var resource = ViewModel.SelectedResource;
            UpdateSelectedResource(resource);
        }
    }

    private void UpdateSelectedResource(ResourceKey resource)
    {
        _entityEditor.ClearComponentListPanel();

        if (resource.IsEmpty)
        {
            return;
        }

        var factory = _inspectorService.InspectorFactory;

        List<UIElement> inspectorElements = new();

        // Create the resource name inspector displayed at the top of the inspector panel
        var nameInspectorResult = factory.CreateResourceNameInspector(resource);
        if (nameInspectorResult.IsFailure)
        {
            _logger.LogError(nameInspectorResult, $"Failed to create resource name inspector for resource: {resource}");
            return;
        }
        var nameInspector = nameInspectorResult.Value as UserControl;
        Guard.IsNotNull(nameInspector);

        inspectorElements.Add(nameInspector);

        // Create the resource inspector (if one is implemented) for the selected resource
        var resourceInspectorResult = factory.CreateResourceInspector(resource);
        if (resourceInspectorResult.IsSuccess)
        {
            var resourceInspector = resourceInspectorResult.Value as UserControl;
            Guard.IsNotNull(resourceInspector);

            inspectorElements.Add(resourceInspector);
        }

        // Create the component list view for the selected resource
        var componentListResult = factory.CreateComponentListView(resource);
        if (componentListResult.IsSuccess)
        {
            var entityInspector = componentListResult.Value as UserControl;
            Guard.IsNotNull(entityInspector);

            inspectorElements.Add(entityInspector);
        }

        _entityEditor.PopulateComponentsPanel(inspectorElements);
    }
}

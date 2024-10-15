using Celbridge.Inspector.ViewModels;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Inspector.Views;

public sealed partial class InspectorPanel : UserControl, IInspectorPanel
{
    private readonly IInspectorService _inspectorService;
    public InspectorPanelViewModel ViewModel { get; }

    public LocalizedString TitleString => _stringLocalizer.GetString($"InspectorPanel_Title");

    private IStringLocalizer _stringLocalizer;

    private StackPanel _inspectorContainer;

    public InspectorPanel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
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

        _inspectorContainer = new StackPanel()
            .Grid(row: 1)
            .Orientation(Orientation.Vertical);

        var panelGrid = new Grid()
            .RowDefinitions("40, *")
            .Children(titleBar, _inspectorContainer);
           
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
        _inspectorContainer.Children.Clear();

        var factory = _inspectorService.InspectorFactory;

        // Create the generic resource inspector displayed at the top of the inspector panel
        var createGenericResult = factory.CreateGenericInspector(resource);
        if (createGenericResult.IsFailure)
        {
            return;
        }
        var genericInspector = createGenericResult.Value as UserControl;
        Guard.IsNotNull(genericInspector);

        _inspectorContainer.Children.Add(genericInspector);
    }
}

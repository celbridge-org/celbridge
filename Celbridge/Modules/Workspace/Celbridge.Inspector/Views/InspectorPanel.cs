using Celbridge.Inspector.ViewModels;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Inspector.Views;

public sealed partial class InspectorPanel : UserControl, IInspectorPanel
{
    private readonly IInspectorService _inspectorService;
    public InspectorPanelViewModel ViewModel { get; }

    public LocalizedString TitleString => _stringLocalizer.GetString($"InspectorPanel_Title");

    private IStringLocalizer _stringLocalizer;

    private Grid _inspectorContainer;

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

        _inspectorContainer = new Grid()
            .Grid(row: 1);

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
        var factory = _inspectorService.InspectorFactory;
        var inspector = factory.CreateInspector(resource) as UserControl;

        _inspectorContainer.Children.Clear();
        _inspectorContainer.Children.Add(inspector);
    }
}

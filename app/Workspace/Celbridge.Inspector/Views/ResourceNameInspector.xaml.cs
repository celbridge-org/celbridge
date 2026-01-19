using Celbridge.Inspector.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Inspector.Views;

public sealed partial class ResourceNameInspector : UserControl, IInspector
{
    private readonly IStringLocalizer _stringLocalizer;

    public ResourceNameInspectorViewModel ViewModel => (DataContext as ResourceNameInspectorViewModel)!;

    private string SelectFileString => _stringLocalizer.GetString("InspectorPanel_SelectFile");
    
    public ResourceKey Resource
    {
        set => ViewModel.Resource = value;
        get => ViewModel.Resource;
    }

    // Code gen requires a parameterless constructor
    public ResourceNameInspector()
    {
        throw new NotImplementedException();
    }

    public ResourceNameInspector(ResourceNameInspectorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.Resource))
        {
            if (!ViewModel.Resource.IsEmpty)
            {
                ToolTipService.SetPlacement(ResourceNameText, PlacementMode.Bottom);
                ToolTipService.SetToolTip(ResourceNameText, ViewModel.Resource);
            }
        }
    }
}


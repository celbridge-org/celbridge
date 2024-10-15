using Celbridge.UserInterface;
using CommunityToolkit.Mvvm.ComponentModel;

using Path = System.IO.Path;

namespace Celbridge.Inspector.ViewModels;

public partial class InspectorViewModel : ObservableObject
{
    private readonly IIconService _iconService;

    [ObservableProperty]
    private ResourceKey _resource;

    [ObservableProperty]
    private IconDefinition _icon;

    public InspectorViewModel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        _iconService = serviceProvider.GetRequiredService<IIconService>();

        _icon = _iconService.DefaultFileIcon;

        PropertyChanged += InspectorViewModel_PropertyChanged;
    }

    private void InspectorViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Resource))
        {
            var fileExtension = Path.GetExtension(Resource);

            var getResult = _iconService.GetIconForFileExtension(fileExtension);            
            if (getResult.IsSuccess)
            {
                Icon = getResult.Value;
            }
        }
    }
}

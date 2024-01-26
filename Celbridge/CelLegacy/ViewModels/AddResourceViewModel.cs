using System.Collections.ObjectModel;

namespace CelLegacy.ViewModels;

public partial class AddResourceViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly IResourceTypeService _resourceTypeService;
    private readonly IResourceService _resourceService;

    public AddResourceViewModel(IProjectService projectService, IResourceTypeService resourceTypeService, IResourceService resourceService)
    {
        _projectService = projectService;
        _resourceTypeService = resourceTypeService;
        _resourceService = resourceService;

        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ResourceName))
            {
                CanAddResource = !string.IsNullOrEmpty(ResourceName);
            }
        };

        ResourceTypeNames = new ObservableCollection<string>();
        _resourceTypes = new List<Type>();

        foreach (var type in _resourceTypeService.ResourceTypes)
        {
            if (type == typeof(Project))
            { 
                continue; 
            }

            var result = _resourceTypeService.GetResourceTypeInfo(type);
            if (result is ErrorResult<ResourceTypeAttribute> error)
            {
                Log.Error(error.Message);
                continue;
            }

            Guard.IsNotNull(result.Data);

            var typeName = result.Data.Name;
            ResourceTypeNames.Add(typeName);
            _resourceTypes.Add(type);
        }
        SelectedTypeIndex = 0;
    }

    private string _resourceName = string.Empty;
    public string ResourceName
    {
        get { return _resourceName; }
        set
        {
            SetProperty(ref _resourceName, value);
        }
    }

    [ObservableProperty]
    private bool _canAddResource;

    [ObservableProperty]
    private ObservableCollection<string> _resourceTypeNames;
    private List<Type> _resourceTypes;

    private int _selectedTypeIndex;
    public int SelectedTypeIndex
    {
        get => _selectedTypeIndex;
        set
        {
            SetProperty(ref _selectedTypeIndex, value);
        }
    }

    public ICommand AddResourceCommand => new RelayCommand(AddResource_Executed);
    private void AddResource_Executed()
    {
        var project = _projectService.ActiveProject;
        Guard.IsNotNull(project);

        var pathResult = _resourceService.GetPathForNewResource(project, ResourceName);
        if (pathResult is ErrorResult<string> error)
        {
            Log.Error($"{error.Message}");
            return;
        }
        var path = pathResult.Data!;
        Guard.IsNotNull(path);

        var resourceType = _resourceTypes[SelectedTypeIndex];
        Guard.IsNotNull(resourceType);

        var createResult = _resourceService.CreateResource(resourceType, path);
        if (createResult.Failure)
        {
            Log.Error($"Failed to create '{resourceType}' resource at '{path}'");
            return;
        }
    }
}

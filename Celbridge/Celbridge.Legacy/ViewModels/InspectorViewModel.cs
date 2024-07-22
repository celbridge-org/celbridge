using Celbridge.Messaging;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Scripting.Utils;
using System.ComponentModel;
using Path = System.IO.Path;

namespace Celbridge.Legacy.ViewModels;

public partial class InspectorViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IInspectorService _inspectorService;
    private readonly ISettingsService _settingsService;
    private readonly IProjectService _projectService;
    private readonly IResourceService _resourceService;
    private readonly IResourceTypeService _resourceTypeService;
    private readonly IDialogService _dialogService;

    public InspectorViewModel(IMessengerService messengerService,
        IInspectorService inspectorService,
        ISettingsService settingsService, 
        IProjectService projectService,
        IResourceService resourceService,
        IResourceTypeService resourceTypeService,
        IDialogService dialogService)
    {
        _messengerService = messengerService;
        _inspectorService = inspectorService;
        _settingsService = settingsService;
        _projectService = projectService;
        _resourceService = resourceService;
        _resourceTypeService = resourceTypeService;
        _dialogService = dialogService;

        _messengerService.Register<SelectedEntityChangedMessage>(this, OnSelectedEntityChanged);
    }

    public ItemCollection? ItemCollection { get; set; }

    [ObservableProperty]
    private bool _hasSelectedEntity;

    [ObservableProperty]
    private IEntity? _selectedEntity;

    [ObservableProperty]
    private string? _typeName;

    private string _typeIcon = "Help";
    public string TypeIcon
    {
        get => _typeIcon;
        set => SetProperty(ref _typeIcon, value);
    }

    public ICommand OpenFolderCommand => new RelayCommand(OpenFolder_Executed);
    private void OpenFolder_Executed()
    {
        // Todo: Open the folder that contains the resource, not the project folder
        if (_projectService.ActiveProject != null)
        {
            var projectPath = _projectService.ActiveProject.ProjectPath;
            var projectFolder = Path.GetDirectoryName(projectPath);
            Guard.IsNotNull(projectFolder);

            _dialogService.OpenFileExplorer(projectFolder);
        }
    }

    public ICommand DeleteEntityCommand => new RelayCommand(DeleteEntity_Executed);
    private void DeleteEntity_Executed()
    {
        if (SelectedEntity == null)
        {
            return;
        }

        // Todo: Find a more generic way to delete entities

        var project = _projectService.ActiveProject;
        Guard.IsNotNull(project);

        switch (SelectedEntity)
        {
            case Resource resource:
                {
                    var result = _resourceService.DeleteResource(project, resource);
                    if (result is ErrorResult error)
                    {
                        Log.Error(error.Message);
                    }
                }
                break;
        }
    }

    private void OnSelectedEntityChanged(object r, SelectedEntityChangedMessage m)
    {
        SelectedEntity = m.Entity;
        HasSelectedEntity = SelectedEntity != null;

        if (SelectedEntity != null)
        {
            if (SelectedEntity is Project)
            {
                TypeName = nameof(Projects);
                TypeIcon = "PreviewLink";
            }
            else if (SelectedEntity is Resource)
            {
                var typeInfoResult = _resourceTypeService.GetResourceTypeInfo(SelectedEntity.GetType());
                if (typeInfoResult.Success)
                {
                    TypeName = typeInfoResult.Data!.Name;
                    TypeIcon = typeInfoResult.Data!.Icon ?? "Help";
                }
            }

            PopulatePropertyListView();
        }
        else
        {
            TypeName = null;
        }
    }

    private void PopulatePropertyListView()
    {
        // Remove any existing property user controls
        Guard.IsNotNull(ItemCollection);
        ItemCollection.Clear();

        Guard.IsNotNull(SelectedEntity);

        var result = PropertyViewUtils.CreatePropertyViews(SelectedEntity, PropertyContext.Record, OnPropertyChanged);
        if (result is ErrorResult<List<UIElement>> error)
        {
            Log.Error(error.Message);
            return;
        }

        var views = result.Data!;
        ItemCollection.AddRange(views);
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var property = sender as Property;
        Guard.IsNotNull(property);

        var entity = property.Object as IEntity;
        Guard.IsNotNull(entity);

        Guard.IsNotNull(e.PropertyName);

        var message = new EntityPropertyChangedMessage(entity, e.PropertyName);
        _messengerService.Send(message);
    }

    public ICommand CollapseCommand => new RelayCommand(Collapse_Executed);

    private void Collapse_Executed()
    {
        Guard.IsNotNull(_settingsService.EditorSettings);

        // Toggle the left toolbar expanded state
        _settingsService.EditorSettings.IsRightPanelVisible = false;
    }
}

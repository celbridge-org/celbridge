using Celbridge.Models;
using Celbridge.Services;
using Celbridge.Utils;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Scripting.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;

namespace Celbridge.ViewModels
{
    public partial class InspectorViewModel : ObservableObject
    {
        private readonly IMessenger _messengerService;
        private readonly IInspectorService _inspectorService;
        private readonly ISettingsService _settingsService;
        private readonly IProjectService _projectService;
        private readonly IResourceService _resourceService;
        private readonly IResourceTypeService _resourceTypeService;
        private readonly ICelTypeService _celTypeService;
        private readonly ICelScriptService _celScriptService;
        private readonly IDialogService _dialogService;

        public InspectorViewModel(IMessenger messengerService,
            IInspectorService inspectorService,
            ISettingsService settingsService, 
            IProjectService projectService,
            IResourceService resourceService,
            IResourceTypeService resourceTypeService,
            ICelTypeService celTypeService,
            ICelScriptService celScriptService,
            IDialogService dialogService)
        {
            _messengerService = messengerService;
            _inspectorService = inspectorService;
            _settingsService = settingsService;
            _projectService = projectService;
            _resourceService = resourceService;
            _resourceTypeService = resourceTypeService;
            _celTypeService = celTypeService;
            _celScriptService = celScriptService;
            _dialogService = dialogService;

            _messengerService.Register<SelectedEntityChangedMessage>(this, OnSelectedEntityChanged);
        }

        public ItemCollection ItemCollection { get; set; }

        [ObservableProperty]
        private bool _hasSelectedEntity;

        [ObservableProperty]
        private IEntity _selectedEntity;

        [ObservableProperty]
        private string _typeName;

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
                        if (result.Failure)
                        {
                            var error = result as ErrorResult;
                            Log.Error(error.Message);
                        }
                    }
                    break;

                case ICelScriptNode cel:
                    {
                        var result = _celScriptService.DeleteCel(cel);
                        if (result.Failure)
                        {
                            var error = result as ErrorResult;
                            Log.Error(error.Message);
                        }
                    }
                    break;
            }
        }

        private void OnSelectedEntityChanged(object r, SelectedEntityChangedMessage m)
        {
            SelectedEntity = m.Value;
            HasSelectedEntity = SelectedEntity != null;

            if (SelectedEntity != null)
            {
                if (SelectedEntity is Project)
                {
                    TypeName = nameof(Project);
                    TypeIcon = "PreviewLink";
                }
                else if (SelectedEntity is Resource)
                {
                    var typeInfoResult = _resourceTypeService.GetResourceTypeInfo(SelectedEntity.GetType());
                    if (typeInfoResult.Success)
                    {
                        TypeName = typeInfoResult.Data.Name;
                        TypeIcon = typeInfoResult.Data.Icon ?? "Help";
                    }
                }
                else if (SelectedEntity is ICelScriptNode)
                {
                    var celTypeResult = _celTypeService.GetCelType(SelectedEntity.GetType());
                    if (celTypeResult.Success)
                    {
                        var celType = celTypeResult.Data;

                        TypeName = celType.Name;
                        TypeIcon = celType.Icon ?? "Help";
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
            ItemCollection.Clear();

            var result = PropertyViewUtils.CreatePropertyViews(SelectedEntity, PropertyContext.Record, OnPropertyChanged);
            if (result.Failure)
            {
                var error = result as ErrorResult<List<UIElement>>;
                Log.Error(error.Message);
                return;
            }

            var views = result.Data;
            ItemCollection.AddRange(views);
        }

        private void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var property = sender as Property;
            Guard.IsNotNull(property);

            var entity = property.Object as IEntity;
            Guard.IsNotNull(entity);

            var message = new EntityPropertyChangedMessage(entity, e.PropertyName);
            _messengerService.Send(message);
        }

        public ICommand CollapseCommand => new RelayCommand(Collapse_Executed);

        private void Collapse_Executed()
        {
            // Toggle the left toolbar expanded state
            _settingsService.EditorSettings.RightPanelExpanded = false;
        }
    }
}

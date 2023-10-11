using Celbridge.Models;
using Celbridge.Services;
using Celbridge.Utils;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Celbridge.ViewModels
{
    public partial class TextFileDocumentViewModel : ObservableObject, ISaveData
    {
        private readonly IMessenger _messengerService;
        private readonly IProjectService _projectService;
        private readonly IDocumentService _documentService;
        private readonly IResourceService _resourceService;
        private readonly ISaveDataService _saveDataService;

        private bool _isSavingContent;
        private bool _isLoadingContent;

        private string _path;

        public TextFileDocumentViewModel(IMessenger messengerService,
                                         IProjectService projectService,
                                         IDocumentService documentService,
                                         IResourceService resourceService,
                                         ISaveDataService saveDataService)
        {
            _messengerService = messengerService;
            _projectService = projectService;
            _documentService = documentService;
            _resourceService = resourceService;
            _saveDataService = saveDataService;

            _messengerService.Register<ResourcesChangedMessage>(this, OnResourcesChanged);
            _messengerService.Register<FileChangedMessage>(this, OnFileChanged);

            PropertyChanged += TextFileDocumentViewModel_PropertyChanged;
        }

        private void OnResourcesChanged(object recipient, ResourcesChangedMessage message)
        {
            foreach (var resourceId in message.Deleted)
            {
                if (Document.DocumentEntity.Id == resourceId)
                {
                    CloseDocumentCommand.Execute(null);
                }
            }
        }

        private void OnFileChanged(object recipient, FileChangedMessage message)
        {
            if (message.Path == _path)
            {
                if (_isSavingContent)
                {
                    // This event was caused by this class saving the file recently, so we're already up to date.
                    _isSavingContent = false;
                }
                else
                {
                    _ = LoadDocumentAsync();
                }
            }
        }

        private IDocument _document;
        public IDocument Document 
        {
            get => _document;
            set
            {
                // Property can only be set once
                Guard.IsNull(_document);
                _document = value;
            }
        }

        public string Name => Document.DocumentEntity.Name;

        private string _content;
        public string Content
        {
            get => _content;
            set
            {
                SetProperty(ref _content, value);
            }
        }

        public IAsyncRelayCommand CloseDocumentCommand => new AsyncRelayCommand(OnCloseDocument_Executed);
        private async Task OnCloseDocument_Executed()
        {
            _messengerService.Unregister<ResourcesChangedMessage>(this);
            _messengerService.Unregister<FileChangedMessage>(this);

            // Delay closing until any pending save operation has completed
            while (_saveDataService.IsPendingSave(this))
            {
                await Task.Delay(50);
            }

            // Close the document and remove it from the auto reload list
            _documentService.CloseDocument(_document.DocumentEntity, false);
        }

        public async Task<Result> LoadDocumentAsync()
        {
            var fileResource = Document.DocumentEntity as FileResource;
            Guard.IsNotNull(fileResource);

            var project = _projectService.ActiveProject;
            Guard.IsNotNull(project);

            var pathResult = _resourceService.GetResourcePath(project, fileResource);
            if (pathResult.Failure)
            {
                var error = pathResult as ErrorResult<string>;
                Log.Error(error.Message);
                return new ErrorResult(error.Message);
            }

            _path = pathResult.Data;
            if (!File.Exists(_path))
            {
                return new ErrorResult($"Failed to load content. File '{_path}' does not exist.");
            }

            try
            {
                _isLoadingContent = true;
                Content = await File.ReadAllTextAsync(_path);
                _isLoadingContent = false;
            }
            catch (Exception ex)
            {
                _isLoadingContent = false;
                return new ErrorResult($"Failed to load content at path '{_path}'. {ex.Message}");
            }

            return new SuccessResult();
        }

        public async Task<Result> SaveAsync()
        {
            var fileResource = Document.DocumentEntity as FileResource;
            Guard.IsNotNull(fileResource);

            var project = _projectService.ActiveProject;
            Guard.IsNotNull(project);

            var pathResult = _resourceService.GetResourcePath(project, fileResource);
            if (pathResult.Failure)
            {
                var error = pathResult as ErrorResult<string>;
                Log.Error(error.Message);
                return new ErrorResult(error.Message);
            }

            return await FileUtils.SaveTextAsync(_path, Content);
        }

        private void TextFileDocumentViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Content) && !_isLoadingContent)
            {
                _saveDataService.RequestSave(this);
            }
        }
    }
}

using Celbridge.Services;
using Celbridge.Utils;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;
using System.Collections.Specialized;
using System.ComponentModel;
using Celbridge.Views;
using System.Numerics;

namespace Celbridge.ViewModels
{
    public partial class CelScriptDocumentViewModel : ObservableObject, ISaveData
    {
        private readonly IMessenger _messengerService;
        private readonly IProjectService _projectService;
        private readonly IDocumentService _documentService;
        private readonly IInspectorService _inspectorService;
        private readonly ICelScriptService _celScriptService;
        private readonly IDialogService _dialogService;
        private readonly ISaveDataService _saveDataService;

        private bool _isLoadingContent;

        private readonly Dictionary<Guid, ICelScriptNode> _cels = new();
        private readonly Dictionary<Guid, CelNode> _celNodes = new();

        public List<CelConnection> CelConnections { get; private set; } = new ();
        public event Action? NodePositionChanged;
        public event Action<Guid>? CelConnectionSelected;

        public CelScriptDocumentViewModel(IMessenger messenger, 
            IProjectService projectService,
            IDocumentService documentService,
            IInspectorService inspectorService,
            ICelScriptService celScriptService,
            IDialogService dialogService,
            ISaveDataService saveDataService)
        {
            _messengerService = messenger;
            _projectService = projectService;
            _documentService = documentService;
            _inspectorService = inspectorService;
            _celScriptService = celScriptService;
            _dialogService = dialogService;
            _saveDataService = saveDataService;

            _messengerService.Register<CelScriptDeletedMessage>(this, OnCelScriptDeleted);
            _messengerService.Register<EntityPropertyChangedMessage>(this, OnEntityPropertyChanged);
            _messengerService.Register<CelConnectionsChangedMessage>(this, OnCelConnectionsChanged);
            _messengerService.Register<SelectedItemUserDataChangedMessage>(this, OnSelectedItemUserDataChanged);

            PropertyChanged += CelScriptDocumentViewModel_PropertyChanged;
        }

        private void OnCelScriptDeleted(object recipient, CelScriptDeletedMessage message)
        {
            if (Document.DocumentEntity.Id == message.ResourceId)
            {
                CloseDocumentCommand.Execute(null);
            }
        }

        private void OnEntityPropertyChanged(object recipient, EntityPropertyChangedMessage message)
        {
            var entity = message.Entity;
            if (entity is ICelScriptNode cel)
            {
                var celScript = cel.CelScript;
                Guard.IsNotNull(celScript);

                if (celScript == CelScript)
                {
                    _saveDataService.RequestSave(this);
                }
            }
        }

        private void OnCelConnectionsChanged(object recipient, CelConnectionsChangedMessage message)
        {
            UpdateCelConnections();
        }

        private void OnSelectedItemUserDataChanged(object recipient, SelectedItemUserDataChangedMessage message)
        {
            var celConnectionId = message.UserData as Guid?;
            if (celConnectionId == null)
            {
                CelConnectionSelected?.Invoke(Guid.Empty);
            }
            else
            {
                CelConnectionSelected?.Invoke((Guid)celConnectionId);
            }
        }

        private IDocument? _document;
        public IDocument Document
        {
            get
            {
                Guard.IsNotNull(_document);
                return _document;
            }
            set
            {
                // Property can only be set once
                Guard.IsNull(_document);
                _document = value;
            }
        }

        public string Name => Document!.DocumentEntity.Name;

        private ICelScript? _celScript;
        public ICelScript CelScript
        {
            get
            {
                Guard.IsNotNull(_celScript);
                return _celScript;
            }
            set
            {
                Guard.IsNotNull(value);
                SetProperty(ref _celScript, value);
            }
        }

        public Vector2 SpawnPosition { get; set; }

        public IAsyncRelayCommand AddCelCommand => new AsyncRelayCommand(OnAddCel_Executed);
        private async Task OnAddCel_Executed()
        {
            Guard.IsNotNull(CelScript);
            await _dialogService.ShowAddCelDialogAsync(CelScript, SpawnPosition);
        }

        public IAsyncRelayCommand CloseDocumentCommand => new AsyncRelayCommand(OnCloseDocument_ExecutedAsync);

        private Canvas? _celCanvas;
        public Canvas CelCanvas 
        {
            get
            {
                Guard.IsNotNull(_celCanvas);
                return _celCanvas;
            }
            set
            {
                _celCanvas = value;
            }
        }

        private async Task OnCloseDocument_ExecutedAsync()
        {
            // Delay closing until any pending save operation has completed
            while (_saveDataService.IsPendingSave(this))
            {
                await Task.Delay(50);
            }

            _messengerService.Unregister<ResourcesChangedMessage>(this);
            _messengerService.Unregister<EntityPropertyChangedMessage>(this);

            // Check if the selected entity is a Cel in this document
            var selectedEntity = _inspectorService.SelectedEntity;
            if (selectedEntity != null)
            {
                foreach (var cell in CelScript.Cels)
                {
                    var entity = cell as IEntity;
                    Guard.IsNotNull(entity);

                    if (selectedEntity == entity)
                    {
                        // Deselect the entity before closing the document
                        _inspectorService.SelectedEntity = null;
                        break;
                    }
                }
            }

            // Close the document and remove it from the auto reload list
            _documentService.CloseDocument(Document.DocumentEntity, false);
        }

        public async Task<Result> LoadAsync()
        {
            try
            {
                _isLoadingContent = true;

                var fileResource = Document.DocumentEntity as FileResource;
                Guard.IsNotNull(fileResource);

                var project = _projectService.ActiveProject;
                Guard.IsNotNull(project);

                var loadResult = await _celScriptService.LoadCelScriptAsync(project, fileResource);

                if (loadResult is ErrorResult<ICelScript> loadError)
                {
                    _isLoadingContent = false;
                    return new ErrorResult(loadError.Message);
                }
                if (loadResult.Data is null)
                {
                    _isLoadingContent = false;
                    return new ErrorResult($"Failed to load CelScript document '{Document.DocumentEntity}'");
                }

                CelScript = loadResult.Data!;
                foreach (var cel in CelScript.Cels)
                {
                    cel.CelScript = CelScript;

                    var addResult = AddCelNode(cel);
                    if (addResult.Failure)
                    {
                        var error = addResult as ErrorResult;
                        return new ErrorResult($"Failed to load CelScript document '{Document.DocumentEntity}'. {error!.Message}");
                    }
                }

                CelScript.Cels.CollectionChanged += CelScript_CollectionChanged;

                _isLoadingContent = false;
            }
            catch (Exception ex)
            {
                _isLoadingContent = false;
                return new ErrorResult($"Failed to load CelScript document '{Document.DocumentEntity}'. {ex.Message}");
            }

            return new SuccessResult();
        }

        public async Task<Result> SaveAsync()
        {
            var fileResource = Document.DocumentEntity as FileResource;
            Guard.IsNotNull(fileResource);

            var project = _projectService.ActiveProject;
            Guard.IsNotNull(project);

            var saveResult = await _celScriptService.SaveCelScriptAsync(project, fileResource, CelScript);
            if (saveResult.Failure)
            {
                var error = saveResult as ErrorResult;
                Log.Error(error!.Message);
            }
            return saveResult;
        }

        private void CelScript_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    Guard.IsNotNull(e.NewItems);
                    foreach (var item in e.NewItems)
                    {
                        var cel = item as CelScriptNode;
                        Guard.IsNotNull(cel);
                        cel.PropertyChanged += Cel_PropertyChanged;

                        var result = AddCelNode(cel);
                        if (result.Success)
                        {
                            _saveDataService.RequestSave(this);
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    Guard.IsNotNull(e.OldItems);
                    foreach (var item in e.OldItems)
                    {
                        var cel = item as CelScriptNode;
                        Guard.IsNotNull(cel);
                        cel.PropertyChanged -= Cel_PropertyChanged;

                        var result = RemoveCelView(cel);
                        if (result.Success)
                        {
                            _saveDataService.RequestSave(this);
                        }
                    }
                    break;
            }
        }

        private Result AddCelNode(ICelScriptNode cel)
        {
            Guard.IsNotNull(cel);

            try
            {
                // Instantiate cel view
                var celNode = new CelNode(cel.X, cel.Y)
                {
                    Name = cel.Name
                };
                celNode.ViewModel.SetCel(cel);
                celNode.ViewModel.CelScriptDocumentViewModel = this; // Todo: Can we decouple these with events?

                // Attach to parent cel canvas
                CelCanvas.Children.Add(celNode);

                // Associate cel with view
                var guid = cel.Id;
                _cels.Add(guid, cel);
                _celNodes.Add(guid, celNode);

                UpdateCelConnections();
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Failed to add Cel View. {ex.Message}");
            }

            return new SuccessResult();
        }

        private Result RemoveCelView(ICelScriptNode cel)
        {
            Guard.IsNotNull(cel);

            try
            {
                if (!_celNodes.TryGetValue(cel.Id, out var celNode))
                {
                    return new ErrorResult($"Failed to remove Cel View. Cel '{cel.Name}' does not have a registered CelNode");
                }

                if (!CelCanvas.Children.Remove(celNode))
                {
                    return new ErrorResult($"Failed to remove CelNode. Cel Canvas does not have a registered child '{cel.Name}'");
                }

                _cels.Remove(cel.Id);
                _celNodes.Remove(cel.Id);

                UpdateCelConnections();
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Failed to remove CelNode. {ex.Message}");
            }

            return new SuccessResult();
        }

        private void Cel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {}

        private void CelScriptDocumentViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CelScript) && !_isLoadingContent)
            {
                _saveDataService.RequestSave(this);
            }
        }

        public void SetCelPosition(Guid entityId, int x, int y)
        {
            if (!_cels.TryGetValue(entityId, out var cel))
            {
                Log.Error("Failed to get position for Cel with id {EntityId}", entityId);
                return;
            }

            if (cel.X == x && cel.Y == y)
            {
                return;
            }

            cel.X = x;
            cel.Y = y;

            NodePositionChanged?.Invoke();

            OnPropertyChanged(nameof(CelScript));
        }

        private void UpdateCelConnections()
        {
            CelConnections.Clear();
            foreach (var kv in _celNodes)
            {
                var celId = kv.Key;
                var celView = kv.Value;

                var celScriptNode = celView.ViewModel.Cel;
                Guard.IsNotNull(celScriptNode);

                var connectedCelIds = (celScriptNode as ICel)!.ConnectedCelIds;
                Guard.IsNotNull(connectedCelIds);

                foreach (var connectedCelId in connectedCelIds)
                {
                    if (_celNodes.TryGetValue(connectedCelId, out var connectedCelView))
                    {
                        var connectedCelScriptNode = connectedCelView.ViewModel.Cel;
                        Guard.IsNotNull(connectedCelScriptNode);

                        var celConnection = new CelConnection(celScriptNode, connectedCelScriptNode);

                        if (CelConnections.Any(c => c.CelConnectionId == celConnection.CelConnectionId))
                        {
                            // Only add the same connection once
                            continue;
                        }

                        CelConnections.Add(celConnection);
                    }
                }
            }

            CelConnections.Sort();
            OnPropertyChanged(nameof(CelConnections));
        }
    }
}

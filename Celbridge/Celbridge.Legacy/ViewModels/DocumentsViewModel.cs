using Celbridge.Messaging;

namespace Celbridge.Legacy.ViewModels;

public partial class DocumentsViewModel : ObservableRecipient
{
    private readonly IMessengerService _messengerService;
    private readonly IDocumentService _documentService;
    private readonly ISettingsService _settingsService;

    public IDocumentsPanelView? DocumentsPanelView { get; internal set; }

    public DocumentsViewModel(IMessengerService messengerService, 
        IDocumentService documentService,
        ISettingsService settingsService)
    {
        _messengerService = messengerService;
        _documentService = documentService;
        _settingsService = settingsService;

        // You have to manually tell the ObservableRecepient to start receiving messages.
        // It's cool being able to switch listening on/off like this but it should default
        // to true as it doesn't work without it and the documentation doesn't mention it.
        IsActive = true;
    }

    protected override void OnActivated()
    {
        _messengerService.Register<DocumentOpenedMessage>(this, OnDocumentOpened);
        _messengerService.Register<DocumentClosedMessage>(this, OnDocumentClosed);
    }

    private void OnDocumentOpened(object r, DocumentOpenedMessage m)
    {
        var document = m.Value;
        Guard.IsNotNull(document);

        Guard.IsNotNull(DocumentsPanelView);
        if (DocumentsPanelView.TryFocusDocumentTab(document))
        {
            return;
        }

        // Log.Information($"Opened document {document.DocumentEntity.Name}");

        // Find the class name of document view
        var resourceTypeName = document.DocumentEntity.GetType().Name;
        var documentTypeName = resourceTypeName.Replace("Resource", string.Empty, StringComparison.InvariantCultureIgnoreCase);
        var documentViewName = $"{documentTypeName}DocumentView";
        var viewTypeName = $"Celbridge.Legacy.Views.{documentViewName}";

        // Instantiate the document view user control
        var type = Type.GetType(viewTypeName);
        if (type == null)
        {
            Log.Error($"Failed to create view for document '{document.DocumentEntity.Name}' of type '{viewTypeName}'");
            return;
        }

        // Instantiate the control for this ViewName
        var userControl = Activator.CreateInstance(type) as TabViewItem; // create a userControl of the class
        Guard.IsNotNull(userControl);

        var documentView = userControl as IDocumentView;
        Guard.IsNotNull(documentView);

        // Let the document view know which document & resource to operate on
        documentView.Document = document;

        async void LoadDocumentAsync()
        {
            var result = await documentView.LoadDocumentAsync();
            if (result is ErrorResult error)
            {
                Log.Error(error.Message);

                if (document.DocumentEntity != null)
                {
                    // Close the document and remove it from the auto reload list
                    _documentService.CloseDocument(document.DocumentEntity, false);
                }
            }

            Guard.IsNotNull(_settingsService.ProjectSettings);
            var openDocuments = _settingsService.ProjectSettings.OpenDocuments;

            Guard.IsNotNull(document.DocumentEntity);
            var documentEntityId = document.DocumentEntity.Id;

            if (!openDocuments.Contains(documentEntityId)) 
            { 
                openDocuments.Add(documentEntityId);
            }

            // Add the tab to the DocumentsPanel
            DocumentsPanelView.OpenDocumentTab(userControl);
        }

        LoadDocumentAsync();
    }

    private void OnDocumentClosed(object r, DocumentClosedMessage m)
    {
        var document = m.Value;

        Guard.IsNotNull(DocumentsPanelView);
        DocumentsPanelView.CloseDocumentTab(document);

        if (!m.AutoReload)
        {
            // Remove this document from the list of documents to auto load when the project opens
            Guard.IsNotNull(_settingsService.ProjectSettings);
            var openDocuments = _settingsService.ProjectSettings.OpenDocuments;
            var documentEntityId = document.DocumentEntity.Id;
            openDocuments.Remove(documentEntityId);
        }
    }
}

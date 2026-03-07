using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Code.ViewModels;

public partial class CodeEditorViewModel : DocumentViewModel
{
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IDocumentsService _documentsService;

    // A cache of the editor text that was last saved to disk.
    // This is the text that is displayed in the preview panel.
    [ObservableProperty]
    private string _cachedText = string.Empty;

    public CodeEditorViewModel(
        ICommandService commandService,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _commandService = commandService;
        _messengerService = messengerService;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;

        // Register for resource change messages
        _messengerService.Register<MonitoredResourceChangedMessage>(this, OnMonitoredResourceChangedMessage);
        _messengerService.Register<DocumentSaveCompletedMessage>(this, OnDocumentSaveCompletedMessage);
    }

    public async Task<Result<string>> LoadDocument()
    {
        try
        {
            // Read the file contents to initialize the text editor
            var text = await File.ReadAllTextAsync(FilePath);

            CachedText = text;

            // Track the initial file state when loading
            UpdateFileTrackingInfo();

            return Result<string>.Ok(text);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to load document file: '{FilePath}'")
                .WithException(ex);
        }
    }

    public async Task<Result> SaveDocument(string text)
    {
        // Don't immediately try to save again if the save fails.
        HasUnsavedChanges = false;
        SaveTimer = 0;

        CachedText = text;

        try
        {
            // Set flag to suppress reload requests triggered by our own save
            IsSavingFile = true;

            await File.WriteAllTextAsync(FilePath, text);

            // Update tracking info BEFORE sending completion message to avoid race condition
            // with file watcher events that may arrive before the message is processed
            UpdateFileTrackingInfo();

            var message = new DocumentSaveCompletedMessage(FileResource);
            _messengerService.Send(message);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to save document file: '{FilePath}'")
                .WithException(ex);
        }
        finally
        {
            IsSavingFile = false;
        }

        return Result.Ok();
    }

    public void OnTextChanged()
    {
        HasUnsavedChanges = true;
        SaveTimer = SaveDelay;
    }

    public void ToggleLayout()
    {
        _commandService.Execute<ISetLayoutCommand>(command =>
        {
            command.Transition = WindowModeTransition.ToggleZenMode;
        });
    }

    public void NavigateToURL(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            // Navigating to an empty URL is a no-op
            return;
        }

        _commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = url;
        });
    }

    private void OnMonitoredResourceChangedMessage(object recipient, MonitoredResourceChangedMessage message)
    {
        // Check if the changed resource is the current document
        if (message.Resource == FileResource)
        {
            // Ignore file watcher events triggered by our own save operation
            if (IsSavingFile)
            {
                return;
            }

            // Check if this change is genuinely different from our last save
            if (IsFileChangedExternally())
            {
                // This is an external change, notify the view to reload
                RaiseReloadRequested();
            }
        }
    }

    private void OnDocumentSaveCompletedMessage(object recipient, DocumentSaveCompletedMessage message)
    {
        // Check if this is a save completion for the current document
        if (message.DocumentResource == FileResource)
        {
            // Update our tracking information after a successful save
            UpdateFileTrackingInfo();
        }
    }

    public override void Cleanup()
    {
        // Unregister message handlers
        _messengerService.UnregisterAll(this);
    }
}

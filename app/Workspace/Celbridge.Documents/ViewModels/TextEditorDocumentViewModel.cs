using Celbridge.Documents.Services;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Documents.ViewModels;

public partial class TextEditorDocumentViewModel : DocumentViewModel
{
    private readonly IMessengerService _messengerService;

    public Action<string>? OnSetContent;

    public TextEditorDocumentViewModel(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;

        _messengerService.Register<SetTextDocumentContentMessage>(this, OnSetTextDocumentContentMessage);
    }

    public void SetFileResource(ResourceKey fileResource)
    {
        FileResource = fileResource;
    }

    private void OnSetTextDocumentContentMessage(object recipient, SetTextDocumentContentMessage message)
    {
        if (message.Resource != FileResource)
        {
            return;
        }

        var content = message.Content;

        // Notify the view that the content should be updated
        OnSetContent?.Invoke(content);
    }

    public override void Cleanup()
    {
        _messengerService.UnregisterAll(this);
    }
}

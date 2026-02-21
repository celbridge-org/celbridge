using Celbridge.Documents.Services;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class TextEditorDocumentViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;

    private ResourceKey _fileResource;

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
        _fileResource = fileResource;
    }

    private void OnSetTextDocumentContentMessage(object recipient, SetTextDocumentContentMessage message)
    {
        if (message.Resource != _fileResource)
        {
            return;
        }

        var content = message.Content;

        // Notify the view that the content should be updated
        OnSetContent?.Invoke(content);
    }
}

using Celbridge.BaseLibrary.Documents;
using Celbridge.BaseLibrary.UserInterface;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DocumentsPanelViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IDocumentsService _documentsService;

    // The document tabs are offset to the right when the left panel is
    // visible to avoid overlapping the main menu button.
    [ObservableProperty]
    private bool _isLeftPanelVisible;

    public DocumentsPanelViewModel(
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService,
        IDocumentsService documentsService)
    {
        _messengerService = messengerService;
        _documentsService = documentsService; // Transient instance created via DI

        // Register the project service with the workspace service
        userInterfaceService.WorkspaceService.RegisterService(_documentsService);

        // Listen for Left Panel visibility changes
        // We have to register for this in the constuctor because we send a "fake" visibility changed message
        // when the workspace opens to set the initial state of IsLeftPanelVisible.
        _messengerService.Register<WorkspacePanelVisibilityChangedMessage>(this, OnWorkspacePanelVisibilityChanged);
    }

    public void OnViewLoaded()
    {}

    public void OnViewUnloaded()
    {
        _messengerService.Unregister<WorkspacePanelVisibilityChangedMessage>(this);
    }

    private void OnWorkspacePanelVisibilityChanged(object recipient, WorkspacePanelVisibilityChangedMessage message)
    {
        IsLeftPanelVisible = message.IsLeftPanelVisible;
    }
}

using Celbridge.BaseLibrary.Documents;
using Celbridge.BaseLibrary.UserInterface;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DocumentsPanelViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IDocumentsService _documentsService;

    //
    // We offset the tab strips header & footer when the left and right panels are visible
    // to avoid overlapping the workspace buttons.
    //

    [ObservableProperty]
    private bool _isLeftPanelVisible;

    [ObservableProperty]
    private bool _isRightPanelVisible;

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
        IsRightPanelVisible = message.IsRightPanelVisible;
    }
}

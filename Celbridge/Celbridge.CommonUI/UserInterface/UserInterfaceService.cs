using Celbridge.BaseLibrary.Messaging;
using Celbridge.CommonUI.Messages;
using Celbridge.CommonUI.Views;
using CommunityToolkit.Diagnostics;

namespace Celbridge.CommonUI.UserInterface;

public class UserInterfaceService : IUserInterfaceService
{
    private IMessengerService _messengerService;
    private WorkspaceView? _workspaceView;

    public UserInterfaceService(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public WorkspaceView WorkspaceView 
    {
        get
        {
            Guard.IsNotNull(_workspaceView);
            return _workspaceView;
        }

#pragma warning disable CS8767 // Nullability of reference types in type of parameter doesn't match implicitly implemented member
        set
#pragma warning restore CS8767
        {
            // WorkspaceView can only be set once, during application startup.
            Guard.IsNull(_workspaceView);
            _workspaceView = value;

            // Notify listeners that the WorkspaceView has loaded
            var message = new WorkspaceViewLoadedMessage(_workspaceView);
            _messengerService.Send(message);
        }
    }
}

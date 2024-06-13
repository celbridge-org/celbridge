using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.UserInterface;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Project.ViewModels;

public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly ILoggingService _loggingService;
    private readonly IProjectService _projectService;

    public ObservableCollection<IResource> Resources => _projectService.ResourceRegistry.Resources;

    public ResourceTreeViewModel(
        IMessengerService messengerService,
        ILoggingService loggingService,
        IUserInterfaceService userInterface)
    {
        _messengerService = messengerService;
        _loggingService = loggingService;

        _projectService = userInterface.WorkspaceService.ProjectService;

        _messengerService.Register<RequestProjectRefreshMessage>(this, OnRefreshProjectRequested);
    }

    public void ResourceTreeView_Unloaded()
    {
        _messengerService.Unregister<RequestProjectRefreshMessage>(this);
    }

    private void OnRefreshProjectRequested(object recipient, RequestProjectRefreshMessage message)
    {
        var updateResult = _projectService.ResourceRegistry.UpdateRegistry();
        if (updateResult.IsFailure)
        {
            _loggingService.Error(updateResult.Error);
        }
    }
}

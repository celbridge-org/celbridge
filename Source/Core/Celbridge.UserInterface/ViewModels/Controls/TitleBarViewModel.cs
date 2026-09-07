using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.UserInterface.ViewModels.Controls;

/// <summary>
/// ViewModel for the TitleBar control.
/// </summary>
public partial class TitleBarViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDispatcher _dispatcher;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isWorkspaceLoaded;

    [ObservableProperty]
    private bool _hasSaveFailures;

    [ObservableProperty]
    private string _saveFailureMessage = string.Empty;

    public TitleBarViewModel(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        IStringLocalizer stringLocalizer,
        IDispatcher dispatcher)
    {
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _stringLocalizer = stringLocalizer;
        _dispatcher = dispatcher;
    }

    public void OnLoaded()
    {
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloaded);
        _messengerService.Register<PendingSaveCountMessage>(this, OnPendingSaveCount);
        _messengerService.Register<WorkspaceItemSaveFailuresChangedMessage>(this, OnSaveFailuresChanged);

        IsWorkspaceLoaded = _workspaceWrapper.IsWorkspaceLoaded;
    }

    public void OnUnloaded()
    {
        _messengerService.UnregisterAll(this);
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        IsWorkspaceLoaded = true;
    }

    private void OnWorkspaceUnloaded(object recipient, WorkspaceUnloadedMessage message)
    {
        IsWorkspaceLoaded = false;

        // The failures belonged to the workspace that is going away.
        HasSaveFailures = false;
        SaveFailureMessage = string.Empty;
    }

    private void OnSaveFailuresChanged(object recipient, WorkspaceItemSaveFailuresChangedMessage message)
    {
        var failingResources = message.FailingResources;
        var failureMessage = ComposeSaveFailureMessage(failingResources);

        // Raised from the workspace update loop, which does not run on the UI thread.
        _dispatcher.TryEnqueue(() =>
        {
            HasSaveFailures = failingResources.Count > 0;
            SaveFailureMessage = failureMessage;
        });
    }

    // One failing resource is named, which is all the user needs to find it. Several are a count, since
    // the names do not fit a tooltip and each one carries its own marker on its tab.
    private string ComposeSaveFailureMessage(IReadOnlyList<ResourceKey> failingResources)
    {
        if (failingResources.Count == 0)
        {
            return string.Empty;
        }

        if (failingResources.Count == 1)
        {
            return _stringLocalizer.GetString("SaveStatus_Failed_Single", failingResources[0].ResourceName);
        }

        return _stringLocalizer.GetString("SaveStatus_Failed_Multiple", failingResources.Count);
    }

    private void OnPendingSaveCount(object recipient, PendingSaveCountMessage message)
    {
        // Raised from the workspace update loop, which does not run on the UI thread.
        _dispatcher.TryEnqueue(() => IsSaving = message.Count > 0);
    }
}



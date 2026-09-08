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
        IStringLocalizer stringLocalizer)
    {
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _stringLocalizer = stringLocalizer;
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

        // The save state belonged to the workspace that is going away.
        IsSaving = false;
        ApplySaveFailures(Array.Empty<ResourceKey>());
    }

    private void OnSaveFailuresChanged(object recipient, WorkspaceItemSaveFailuresChangedMessage message)
    {
        ApplySaveFailures(message.FailingResources);
    }

    private void ApplySaveFailures(IReadOnlyList<ResourceKey> failingResources)
    {
        HasSaveFailures = failingResources.Count > 0;
        SaveFailureMessage = ComposeSaveFailureMessage(failingResources);
    }

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
        IsSaving = message.Count > 0;
    }
}



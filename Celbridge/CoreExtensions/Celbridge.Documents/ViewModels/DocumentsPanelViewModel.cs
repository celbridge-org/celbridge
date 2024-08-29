using Celbridge.Commands;
using Celbridge.Resources;
using Celbridge.Settings;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DocumentsPanelViewModel : ObservableObject
{
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IEditorSettings _editorSettings;

    public bool IsLeftPanelVisible => _editorSettings.IsLeftPanelVisible;

    public bool IsRightPanelVisible => _editorSettings.IsRightPanelVisible;

    public DocumentsPanelViewModel(
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IEditorSettings editorSettings)
    {
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;

        _editorSettings = editorSettings;

        var settings = _editorSettings as INotifyPropertyChanged;
        Guard.IsNotNull(settings);
        settings.PropertyChanged += EditorSettings_PropertyChanged;
    }

    public void OnViewLoaded()
    {}

    public void OnViewUnloaded()
    {
        var settings = _editorSettings as INotifyPropertyChanged;
        Guard.IsNotNull(settings);
        settings.PropertyChanged -= EditorSettings_PropertyChanged;
    }

    private void EditorSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 
        // Map the changed editor setting to the corresponding view model property.
        //
        if (e.PropertyName == nameof(IEditorSettings.IsLeftPanelVisible))
        {
            OnPropertyChanged(nameof(IsLeftPanelVisible));
        }
        else if (e.PropertyName == nameof(IEditorSettings.IsRightPanelVisible))
        {
            OnPropertyChanged(nameof(IsRightPanelVisible));
        }
    }

    public void OnCloseDocumentRequested(ResourceKey fileResource)
    {
        _commandService.Execute<ICloseDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
        });
    }
}

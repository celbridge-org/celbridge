using Celbridge.BaseLibrary.Documents;
using Celbridge.BaseLibrary.Settings;
using Celbridge.BaseLibrary.UserInterface;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DocumentsPanelViewModel : ObservableObject
{
    private readonly IDocumentsService _documentsService;
    private readonly IEditorSettings _editorSettings;

    public bool IsLeftPanelVisible => _editorSettings.IsLeftPanelVisible;

    public bool IsRightPanelVisible => _editorSettings.IsRightPanelVisible;

    public DocumentsPanelViewModel(
        IUserInterfaceService userInterfaceService,
        IEditorSettings editorSettings,
        IDocumentsService documentsService)
    {
        _editorSettings = editorSettings;
        _documentsService = documentsService; // Transient instance created via DI

        // Register the project service with the workspace service
        userInterfaceService.WorkspaceService.RegisterService(_documentsService);

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
}

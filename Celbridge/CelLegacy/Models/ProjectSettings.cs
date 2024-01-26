using System.Collections.ObjectModel;

namespace CelLegacy.Models;

public partial class ProjectSettings : ObservableObject
{
    [ObservableProperty]
    private Guid _projectId;

    [ObservableProperty]
    private ObservableCollection<Guid> _openDocuments = new ();

    [ObservableProperty]
    private Guid _selectedEntity;

    public ProjectSettings()
    {
        // Required for serialization.

        // Forward changes to the open documents collection as property changes.
        _openDocuments.CollectionChanged += (s, e) => OnPropertyChanged(nameof(OpenDocuments));
    }
}

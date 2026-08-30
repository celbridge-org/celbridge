using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Celbridge.Projects;
using Celbridge.UserInterface;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Drives the Shortcuts section: the document shortcut buttons the Utility Rail offers, each opening one
/// project resource. The cards are the order the rail draws them in, and the workspace picks the changes
/// up when the project is reloaded.
/// </summary>
public class DocumentShortcutsSectionViewModel : ProjectSettingsSectionViewModel
{
    private readonly IIconService _iconService;

    // Set while the section rebuilds itself from the config, so populating the collection does not write
    // what it just read back into the draft.
    private bool _suppressCommit;

    public ObservableCollection<DocumentShortcutViewModel> Shortcuts { get; } = new();

    public string EmptyText => ProjectSettingsLabels.ShortcutsEmpty;

    public string AddShortcutText => ProjectSettingsLabels.AddShortcut;

    public DocumentShortcutsSectionViewModel(ProjectSettingsContext context, IIconService iconService)
        : base(context)
    {
        _iconService = iconService;

        Shortcuts.CollectionChanged += Shortcuts_CollectionChanged;
    }

    public override void Load()
    {
        var config = GetConfig();

        _suppressCommit = true;
        try
        {
            // Clearing raises a reset, which reports no old items, so the outgoing cards are detached here.
            foreach (var shortcut in Shortcuts)
            {
                shortcut.PropertyChanged -= Shortcut_PropertyChanged;
            }

            Shortcuts.Clear();

            if (config is not null)
            {
                foreach (var documentShortcut in config.DocumentShortcuts)
                {
                    Shortcuts.Add(CreateShortcut(documentShortcut));
                }
            }
        }
        finally
        {
            _suppressCommit = false;
        }
    }

    /// <summary>
    /// Appends a blank shortcut for the user to fill in. Called by the card list's add button.
    /// </summary>
    public void AddShortcut()
    {
        var documentShortcut = new DocumentShortcut
        {
            Resource = string.Empty
        };

        Shortcuts.Add(CreateShortcut(documentShortcut));
    }

    private DocumentShortcutViewModel CreateShortcut(DocumentShortcut documentShortcut)
    {
        var shortcut = new DocumentShortcutViewModel(_iconService, ResourceExists)
        {
            Resource = documentShortcut.Resource,
            Icon = documentShortcut.Icon
        };

        return shortcut;
    }

    // Whether the project holds the resource, which decides if a card reports its file as missing.
    private bool ResourceExists(ResourceKey fileResource)
    {
        var registry = WorkspaceService?.ResourceService.Registry;
        if (registry is null)
        {
            return true;
        }

        return registry.GetResource(fileResource).IsSuccess;
    }

    // The card list adds, deletes and reorders through the collection, so every one of those lands here.
    private void Shortcuts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (DocumentShortcutViewModel shortcut in e.OldItems)
            {
                shortcut.PropertyChanged -= Shortcut_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (DocumentShortcutViewModel shortcut in e.NewItems)
            {
                shortcut.PropertyChanged += Shortcut_PropertyChanged;
            }
        }

        Commit();
    }

    private void Shortcut_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The stored properties only. A card also reports the display properties derived from these,
        // which carry no edit of their own.
        if (e.PropertyName != nameof(DocumentShortcutViewModel.Resource)
            && e.PropertyName != nameof(DocumentShortcutViewModel.Icon))
        {
            return;
        }

        Commit();
    }

    // Writes the whole list to the draft, because deleting and reordering both rewrite it.
    private void Commit()
    {
        if (_suppressCommit)
        {
            return;
        }

        var documentShortcuts = Shortcuts
            .Select(shortcut => shortcut.ToDocumentShortcut())
            .ToList();

        EditConfig(draft => draft.SetDocumentShortcuts(documentShortcuts));
    }
}

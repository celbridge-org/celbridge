using Celbridge.Commands;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.UserInterface.Views.Controls;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Coordinates the Project Settings editor: the section rail, the pending-changes state shared by every
/// section, and the reload gesture. Each section has its own view model, all of which write their edits
/// straight through to the project file; the running workspace only reflects them after a reload.
/// </summary>
public partial class ProjectSettingsEditorViewModel : ObservableObject
{
    // The stable key the Packages section is registered under, used to find its rail row when the
    // contribution issues change.
    private const string PackagesSectionKey = "Packages";

    private readonly ICommandService _commandService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ISettingsService _settings;
    private readonly ProjectSettingsContext _context;

    private bool _loaded;

    // The config instance the sections were last built from, used to skip a rebuild when nothing changed.
    private ProjectConfig? _loadedConfig;

    // Section persistence is enabled only after the restore runs, so restoring the saved section does not
    // immediately rewrite it.
    private bool _sectionPersistenceEnabled;

    // The section to fall back on when the rail reports no selection.
    private SettingsSection? _lastSelectedSection;

    [ObservableProperty]
    private bool _hasPendingChanges;

    [ObservableProperty]
    private IReadOnlyList<SettingsSection> _sections = Array.Empty<SettingsSection>();

    [ObservableProperty]
    private SettingsSection? _selectedSection;

    public InformationSectionViewModel InformationSection { get; }
    public PackagesSectionViewModel PackagesSection { get; }
    public FileEditorsSectionViewModel FileEditorsSection { get; }
    public PagesSectionViewModel PagesSection { get; }
    public FeatureFlagsSectionViewModel FeatureFlagsSection { get; }

    public IRelayCommand ReloadProjectCommand { get; }

    public ProjectSettingsEditorViewModel(
        IProjectService projectService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _commandService = commandService;
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _settings = ServiceLocator.AcquireService<ISettingsService>();
        var packageLocalization = ServiceLocator.AcquireService<IPackageLocalizationService>();
        var fileTypeCatalog = ServiceLocator.AcquireService<IFileTypeCatalog>();

        ReloadProjectCommand = new RelayCommand(ReloadProject);

        _context = new ProjectSettingsContext(workspaceWrapper, projectService, commandService, MarkPending);
        InformationSection = new InformationSectionViewModel(_context);
        PackagesSection = new PackagesSectionViewModel(_context, packageLocalization);
        FileEditorsSection = new FileEditorsSectionViewModel(_context, fileTypeCatalog, _stringLocalizer);
        PagesSection = new PagesSectionViewModel(_context);
        FeatureFlagsSection = new FeatureFlagsSectionViewModel(_context, _stringLocalizer);
    }

    /// <summary>
    /// Supplies the sections to show, in rail order, and selects the one the user last had open. Called by
    /// the view, which is the only place that can build each section's content.
    /// </summary>
    public void InitializeSections(IReadOnlyList<SettingsSection> sections)
    {
        Guard.IsTrue(sections.Count > 0);

        _sectionPersistenceEnabled = false;
        Sections = sections;

        // An unrecognized or empty key lands on the first section, which is what a new project gets.
        var storedKey = _settings.Get(SettingCatalog.Layout.ProjectSettingsSelectedSection);
        var storedSection = sections.FirstOrDefault(section => section.Key == storedKey);

        SelectedSection = storedSection ?? sections[0];

        _sectionPersistenceEnabled = true;
    }

    /// <summary>
    /// Rebuilds every section from the reconciled config. Skipped while there are pending changes so
    /// reopening the editor does not discard the user's uncommitted edits.
    /// </summary>
    public void Refresh()
    {
        if (_loaded
            && HasPendingChanges)
        {
            return;
        }

        // The config instance changes only when a discovery pass runs (initial load or reload), so an
        // unchanged instance means a rebuild would produce identical sections and only reset the editor's
        // view state (expander and scroll positions). Skip it so navigating away and back is lossless.
        var config = _context.GetConfig();
        if (_loaded
            && ReferenceEquals(config, _loadedConfig))
        {
            return;
        }
        _loadedConfig = config;

        InformationSection.Load();
        PackagesSection.Load();
        FileEditorsSection.Load();
        PagesSection.Load();
        FeatureFlagsSection.Load();

        HasPendingChanges = false;
        _loaded = true;

        UpdatePackagesSectionIssue();
    }

    partial void OnSelectedSectionChanged(SettingsSection? value)
    {
        if (value is null)
        {
            // Nothing in the rail clears the selection, but the property is public: hold the invariant that
            // a section is always showing rather than leaving it to callers.
            SelectedSection = _lastSelectedSection;
            return;
        }

        foreach (var section in Sections)
        {
            section.IsSelected = ReferenceEquals(section, value);
        }

        _lastSelectedSection = value;

        if (!_sectionPersistenceEnabled)
        {
            return;
        }

        if (!_settings.IsScopeAvailable(SettingScope.Workspace))
        {
            return;
        }

        // The key rather than the position, so inserting or reordering a section does not land a returning
        // user on a different one.
        _settings.Set(SettingCatalog.Layout.ProjectSettingsSelectedSection, value.Key);
    }

    // Flags the Packages row when any package has a configuration issue. The editor opens on Information,
    // so the issue is otherwise a section away from being seen.
    private void UpdatePackagesSectionIssue()
    {
        var hasIssues = PackagesSection.Packages.Any(package => package.HasIssues);

        foreach (var section in Sections)
        {
            if (section.Key == PackagesSectionKey)
            {
                section.HasIssue = hasIssues;
            }
        }
    }

    // Any section edit marks the editor pending, so it can show that a reload is needed.
    private void MarkPending()
    {
        HasPendingChanges = true;
    }

    private void ReloadProject()
    {
        _commandService.Execute<IReloadProjectCommand>();
    }
}

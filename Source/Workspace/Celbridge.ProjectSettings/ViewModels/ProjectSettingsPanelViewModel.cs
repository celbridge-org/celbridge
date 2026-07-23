using Celbridge.Commands;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Coordinates the Project Settings panel: the section navigation, the pending-changes state shared by
/// every section, and the reload gesture. Each section has its own view model, all of which write their
/// edits straight through to the project file; the running workspace only reflects them after a reload.
/// </summary>
public partial class ProjectSettingsPanelViewModel : ObservableObject
{
    private readonly ICommandService _commandService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ISettingsService _settings;
    private readonly ProjectSettingsContext _context;

    // Stable keys for the three sections, indexed by SelectedSectionIndex. Persisting the key rather than the
    // raw index keeps the restored section correct if the sections are ever reordered.
    private static readonly string[] SectionKeys =
    {
        "Information",
        "Packages",
        "FileEditors",
    };

    private bool _loaded;

    // The config instance the sections were last built from, used to skip a rebuild when nothing changed.
    private ProjectConfig? _loadedConfig;

    // Section persistence is enabled only after the constructor's restore runs, so restoring the saved
    // section does not immediately rewrite it.
    private bool _sectionPersistenceEnabled;

    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private bool _hasPendingChanges;

    [ObservableProperty]
    private int _selectedSectionIndex;

    public bool IsInformationSection => SelectedSectionIndex == 0;
    public bool IsPackagesSection => SelectedSectionIndex == 1;
    public bool IsFileEditorsSection => SelectedSectionIndex == 2;

    /// <summary>
    /// The localized name of the selected section, shown as a large label under the icon strip.
    /// </summary>
    public string ActiveSectionName
    {
        get
        {
            if (IsInformationSection)
            {
                return _stringLocalizer.GetString("ProjectSettings_InformationHeader");
            }
            if (IsPackagesSection)
            {
                return _stringLocalizer.GetString("ProjectSettings_PackagesHeader");
            }

            return _stringLocalizer.GetString("ProjectSettings_FileEditorsHeader");
        }
    }

    /// <summary>
    /// A one-sentence description of what the selected section is for, shown under its name.
    /// </summary>
    public string ActiveSectionDescription
    {
        get
        {
            if (IsInformationSection)
            {
                return _stringLocalizer.GetString("ProjectSettings_InformationDescription");
            }
            if (IsPackagesSection)
            {
                return _stringLocalizer.GetString("ProjectSettings_PackagesDescription");
            }

            return _stringLocalizer.GetString("ProjectSettings_FileEditorsDescription");
        }
    }

    public InformationSectionViewModel InformationSection { get; }
    public PackagesSectionViewModel PackagesSection { get; }
    public FileEditorsSectionViewModel FileEditorsSection { get; }

    public IRelayCommand ReloadProjectCommand { get; }

    public ProjectSettingsPanelViewModel(
        IProjectService projectService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _commandService = commandService;
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _settings = ServiceLocator.AcquireService<ISettingsService>();
        var packageLocalization = ServiceLocator.AcquireService<IPackageLocalizationService>();
        var fileTypeCatalog = ServiceLocator.AcquireService<IFileTypeCatalog>();

        TitleText = _stringLocalizer.GetString("ProjectSettingsPanel_Title");
        ReloadProjectCommand = new RelayCommand(ReloadProject);

        _context = new ProjectSettingsContext(workspaceWrapper, projectService, commandService, MarkPending);
        InformationSection = new InformationSectionViewModel(_context);
        PackagesSection = new PackagesSectionViewModel(_context, packageLocalization);
        FileEditorsSection = new FileEditorsSectionViewModel(_context, fileTypeCatalog, _stringLocalizer);

        RestoreSelectedSection();
    }

    /// <summary>
    /// Rebuilds every section from the reconciled config. Skipped while there are pending changes so
    /// re-showing the panel does not discard the user's uncommitted edits.
    /// </summary>
    public void Refresh()
    {
        if (_loaded
            && HasPendingChanges)
        {
            return;
        }

        // The config instance changes only when a discovery pass runs (initial load or reload), so an
        // unchanged instance means a rebuild would produce identical sections and only reset the panel's
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

        HasPendingChanges = false;
        _loaded = true;

        OnPropertyChanged(nameof(HasPackagesSectionIssues));
    }

    /// <summary>
    /// Whether any package has a configuration issue. Flagged on the Packages tab because the panel opens
    /// on the Information section, so the issue is otherwise a section away from being seen.
    /// </summary>
    public bool HasPackagesSectionIssues => PackagesSection.Packages.Any(package => package.HasIssues);

    public string PackagesSectionIssuesTooltip => ProjectSettingsLabels.PackagesSectionIssue;

    partial void OnSelectedSectionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsInformationSection));
        OnPropertyChanged(nameof(IsPackagesSection));
        OnPropertyChanged(nameof(IsFileEditorsSection));
        OnPropertyChanged(nameof(ActiveSectionName));
        OnPropertyChanged(nameof(ActiveSectionDescription));

        PersistSelectedSection();
    }

    // Restores the last-viewed section from workspace settings. Persistence stays disabled until the restore
    // completes, so selecting the saved section here does not immediately rewrite it. An unrecognized or empty
    // key leaves the default Information section selected.
    private void RestoreSelectedSection()
    {
        var sectionKey = _settings.Get(SettingCatalog.Layout.ProjectSettingsSelectedSection);

        var index = Array.IndexOf(SectionKeys, sectionKey);
        if (index >= 0)
        {
            SelectedSectionIndex = index;
        }

        _sectionPersistenceEnabled = true;
    }

    // Persists the current section so a reload can restore it. The disk write is deferred to the workspace
    // save tick, so it survives the reload as long as a tick runs before the unload, matching how the selected
    // rail utility persists.
    private void PersistSelectedSection()
    {
        if (!_sectionPersistenceEnabled)
        {
            return;
        }

        if (!_settings.IsScopeAvailable(SettingScope.Workspace))
        {
            return;
        }

        var index = SelectedSectionIndex;
        if (index < 0
            || index >= SectionKeys.Length)
        {
            return;
        }

        _settings.Set(SettingCatalog.Layout.ProjectSettingsSelectedSection, SectionKeys[index]);
    }

    // Any section edit marks the panel pending, so the panel can show that a reload is needed.
    private void MarkPending()
    {
        HasPendingChanges = true;
    }

    private void ReloadProject()
    {
        _commandService.Execute<IReloadProjectCommand>();
    }
}

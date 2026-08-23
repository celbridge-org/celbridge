using Celbridge.Commands;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Documents;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Utilities;
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
    private readonly ILogger<ProjectSettingsEditorViewModel> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ISettingsService _settings;
    private readonly ProjectSettingsContext _context;

    private readonly IProjectService _projectService;
    private readonly IMessengerService _messengerService;
    private readonly ILocalFileSystem _fileSystem;

    private bool _loaded;

    // The text last written or last read, so an edit that lands back on it is not a change and a save
    // that would rewrite the same bytes is skipped.
    private string _savedConfigText = string.Empty;

    // The config the running workspace was built from, which the draft is compared against to decide
    // whether a reload would change anything.
    private string _loadedConfigText = string.Empty;

    // The config instance the sections were last built from, used to skip a rebuild when nothing changed.
    private ProjectConfig? _loadedConfig;

    // The working copy the sections edit, replaced each time the project file is read.
    private ProjectConfigDraft? _draft;

    // Section persistence is enabled only after the restore runs, so restoring the saved section does not
    // immediately rewrite it.
    private bool _sectionPersistenceEnabled;

    // The section to fall back on when the rail reports no selection.
    private SettingsSection? _lastSelectedSection;


    // True when the project file did not parse, so the sections have nothing to show and the editor
    // offers to open the file as text instead.
    [ObservableProperty]
    private bool _hasConfigError;

    [ObservableProperty]
    private string _configErrorDetail = string.Empty;

    /// <summary>
    /// True while the sections are the thing to show, which is whenever the config parsed.
    /// </summary>
    public bool HasSections => !HasConfigError;

    /// <summary>
    /// True when the load failure carried a message worth showing verbatim.
    /// </summary>
    public bool HasConfigErrorDetail => !string.IsNullOrWhiteSpace(ConfigErrorDetail);

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
        _projectService = projectService;
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _logger = ServiceLocator.AcquireService<ILogger<ProjectSettingsEditorViewModel>>();
        _fileSystem = ServiceLocator.AcquireService<ILocalFileSystem>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _settings = ServiceLocator.AcquireService<ISettingsService>();
        var packageLocalization = ServiceLocator.AcquireService<IPackageLocalizationService>();
        var fileTypeCatalog = ServiceLocator.AcquireService<IFileTypeCatalog>();

        ReloadProjectCommand = new AsyncRelayCommand(ReloadProjectAsync);

        var project = projectService.CurrentProject;
        HasConfigError = project is not null && !project.ConfigIsHealthy;
        ConfigErrorDetail = project?.ConfigLoadFailure?.MessageChain ?? string.Empty;

        _context = new ProjectSettingsContext(workspaceWrapper, projectService, commandService, MarkPending);
        InformationSection = new InformationSectionViewModel(_context);
        PackagesSection = new PackagesSectionViewModel(_context, packageLocalization);
        FileEditorsSection = new FileEditorsSectionViewModel(_context, fileTypeCatalog, _stringLocalizer);
        PagesSection = new PagesSectionViewModel(_context);
        FeatureFlagsSection = new FeatureFlagsSectionViewModel(_context, _stringLocalizer);

        _messengerService.Register<ResourceChangedMessage>(this, OnResourceChanged);
    }

    /// <summary>
    /// Drops the editor's subscriptions. Called when the document closes.
    /// </summary>
    public void Unregister()
    {
        _messengerService.Unregister<ResourceChangedMessage>(this);
    }

    // An external write to the project file (a git checkout, or the Code Editor in a previous tab) wins,
    // the same way it does for every other document. A write this editor made is filtered by comparing
    // the file to what was last saved.
    private void OnResourceChanged(object recipient, ResourceChangedMessage message)
    {
        var project = _projectService.CurrentProject;
        if (project is null
            || !project.IsProjectFile(message.Resource))
        {
            return;
        }

        var readResult = _fileSystem.ReadAllTextAsync(project.ProjectFilePath).GetAwaiter().GetResult();
        if (readResult.IsFailure)
        {
            return;
        }

        var parseResult = ProjectConfigParser.ParseFromText(readResult.Value);
        if (parseResult.IsFailure)
        {
            return;
        }

        var configText = ProjectConfigSerializer.Serialize(parseResult.Value);
        if (string.Equals(configText, _savedConfigText, StringComparison.Ordinal))
        {
            return;
        }

        _draft = new ProjectConfigDraft(parseResult.Value);
        _context.Draft = _draft;
        _savedConfigText = configText;

        ReloadSections();
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

        LoadDraft();

        InformationSection.Load();
        PackagesSection.Load();
        FileEditorsSection.Load();
        PagesSection.Load();
        FeatureFlagsSection.Load();

        _loaded = true;

        UpdatePackagesSectionIssue();
        OnPropertyChanged(nameof(HasPendingChanges));
    }

    /// <summary>
    /// Whether the config now differs from the one the running workspace was built from, so a reload
    /// would change something.
    /// </summary>
    public bool HasPendingChanges => _draft is not null
        && !string.Equals(_draft.Serialize(), _loadedConfigText, StringComparison.Ordinal);

    // Rebuilds every section from the draft that just replaced the previous one.
    private void ReloadSections()
    {
        InformationSection.Load();
        PackagesSection.Load();
        FileEditorsSection.Load();
        PagesSection.Load();
        FeatureFlagsSection.Load();

        UpdatePackagesSectionIssue();
        OnPropertyChanged(nameof(HasPendingChanges));
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

    /// <summary>
    /// Whether the draft has moved away from what is on disk, so the save tick has something to write.
    /// </summary>
    public bool HasUnsavedChanges => _draft is not null
        && !string.Equals(_draft.Serialize(), _savedConfigText, StringComparison.Ordinal);

    /// <summary>
    /// Writes the draft to the project file. A no-op when the draft matches what is already there.
    /// </summary>
    public async Task<Result> SaveConfigAsync()
    {
        var draft = _draft;
        if (draft is null)
        {
            return Result.Ok();
        }

        var projectFilePath = _projectService.CurrentProject?.ProjectFilePath;
        if (string.IsNullOrEmpty(projectFilePath))
        {
            return Result.Ok();
        }

        var configText = draft.Serialize();
        if (string.Equals(configText, _savedConfigText, StringComparison.Ordinal))
        {
            return Result.Ok();
        }

        var writeResult = await _fileSystem.WriteAllTextAsync(projectFilePath, configText);
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to write the project config: '{projectFilePath}'")
                .WithErrors(writeResult);
        }

        _savedConfigText = configText;

        return Result.Ok();
    }

    // Reads the project file into a fresh draft for the sections to edit. The draft is built from the
    // parsed file rather than the reconciled config, so saving writes back only what the file states
    // rather than every discovered default folded into it.
    private void LoadDraft()
    {
        var projectFilePath = _projectService.CurrentProject?.ProjectFilePath;
        if (string.IsNullOrEmpty(projectFilePath))
        {
            return;
        }

        var parseResult = ProjectConfigParser.ParseFromFile(projectFilePath, _fileSystem);
        if (parseResult.IsFailure)
        {
            return;
        }

        _draft = new ProjectConfigDraft(parseResult.Value);
        _context.Draft = _draft;
        _savedConfigText = _draft.Serialize();

        var loadedConfig = _projectService.CurrentProject?.Config;
        _loadedConfigText = loadedConfig is null ? string.Empty : ProjectConfigSerializer.Serialize(loadedConfig);
    }

    // Every section edit changes the draft, and the pending state is computed from it.
    private void MarkPending()
    {
        OnPropertyChanged(nameof(HasPendingChanges));
    }

    // The reload rebuilds the workspace from the file, so the draft has to reach disk first. The save
    // tick would get there on its own within a second, which is exactly the window a user clicking
    // Reload straight after an edit falls into.
    private async Task ReloadProjectAsync()
    {
        var saveResult = await SaveConfigAsync();
        if (saveResult.IsFailure)
        {
            _logger.LogError(saveResult, "Failed to save the project config before reloading.");
            return;
        }

        _commandService.Execute<IReloadProjectCommand>();
    }

    /// <summary>
    /// Reopens the project file in the Code Editor, which is the only editor that can show a file the
    /// config parser rejected.
    /// </summary>
    public void OpenInCodeEditor(ResourceKey fileResource)
    {
        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.EditorId = DocumentConstants.CodeEditorId;
            command.ForceReload = true;
        });
    }
}

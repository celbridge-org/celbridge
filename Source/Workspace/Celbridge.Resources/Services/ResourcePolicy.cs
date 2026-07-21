using Celbridge.Projects;

namespace Celbridge.Resources.Services;

/// <summary>
/// Concrete policy rule contributed by one of the rule sources. Owns the
/// compiled matcher alongside the metadata exposed by IPolicyRule so the
/// engine can evaluate a candidate path against the pattern in O(1) per rule.
/// </summary>
internal sealed class CompiledPolicyRule : IPolicyRule
{
    public PolicyRuleSource Source { get; }
    public string Pattern { get; }
    public ResourceAction GatedActions { get; }
    public string Description { get; }

    public ResourcePathMatcher? Matcher { get; }

    public CompiledPolicyRule(
        PolicyRuleSource source,
        string pattern,
        ResourceAction gatedActions,
        string description,
        ResourcePathMatcher? matcher)
    {
        Source = source;
        Pattern = pattern;
        GatedActions = gatedActions;
        Description = description;
        Matcher = matcher;
    }
}

/// <summary>
/// Workspace-scoped policy engine. Compiles the rule set once at construction
/// and evaluates against it per call. List and Read visibility follow the set
/// model "(not ignored by the ignore-file, or matched by add) and not matched
/// by remove", below an immutable system tier. Write is gated by the system
/// tier and the lock list.
/// </summary>
public sealed class ResourcePolicy : IResourcePolicy
{
    private readonly List<CompiledPolicyRule> _systemDeny;
    private readonly List<CompiledPolicyRule> _systemAllow;
    private readonly List<CompiledPolicyRule> _add;
    private readonly List<CompiledPolicyRule> _remove;
    private readonly List<CompiledPolicyRule> _lock;

    // Empty until InitializeAsync reads the ignore-file and replaces it, so
    // Evaluate stays safe to call before initialization runs.
    private IIgnoreFileMatcher _ignoreFileMatcher;
    private readonly CompiledPolicyRule _ignoreRule;

    // Static leading paths of the add patterns, used to decide whether the
    // registry walk must descend into an ignored folder to reach an add target.
    // An empty string marks a bare-name add pattern that matches at any depth.
    private readonly IReadOnlyList<string> _addPrefixes;

    private readonly IReadOnlyList<IPolicyRule> _compiledRules;

    private readonly IProject? _project;
    private readonly ResourcesSection _resourcesSection;
    private readonly ILocalFileSystem _fileSystem;

    public IReadOnlyList<IPolicyRule> CompiledRules => _compiledRules;

    public ResourcePolicy(IProjectService projectService, ILocalFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
        _project = projectService.CurrentProject;
        _resourcesSection = _project?.Config.Resources ?? new ResourcesSection();

        // On a healthy load the project file is hidden from the resource tree (see BuildSystemDenyRules);
        // a faulted load leaves it visible so the code editor can repair it.
        _systemDeny = BuildSystemDenyRules(hideProjectFile: _project?.ConfigIsHealthy ?? false);
        _systemAllow = BuildSystemAllowRules();
        _add = CompileProjectRules(
            _resourcesSection.Add,
            PolicyRuleSource.ProjectAdd,
            ResourceAction.List | ResourceAction.Read,
            "Pattern from the project '[resources].add' list.");
        _remove = CompileProjectRules(
            _resourcesSection.Remove,
            PolicyRuleSource.ProjectRemove,
            ResourceAction.List | ResourceAction.Read,
            "Pattern from the project '[resources].remove' list.");
        _lock = CompileProjectRules(
            _resourcesSection.Lock,
            PolicyRuleSource.ProjectLocked,
            ResourceAction.Write,
            "Pattern from the project '[resources].lock' list. The resource is frozen in place.");

        // The ignore-file read happens in InitializeAsync, not here, so
        // construction does no IO. The baseline is an empty ignore set until then.
        _ignoreFileMatcher = new IgnoreFileMatcher(Array.Empty<string>());
        _ignoreRule = new CompiledPolicyRule(
            source: PolicyRuleSource.IgnoreFile,
            pattern: string.IsNullOrEmpty(_resourcesSection.IgnoreFile) ? "(disabled)" : _resourcesSection.IgnoreFile,
            gatedActions: ResourceAction.List | ResourceAction.Read,
            description: "The resource is excluded by the project ignore-file. Add it to '[resources].add' to make it a resource.",
            matcher: null);

        _addPrefixes = BuildAddPrefixes(_resourcesSection.Add);

        var combined = new List<IPolicyRule>();
        combined.AddRange(_systemDeny);
        combined.AddRange(_systemAllow);
        combined.Add(_ignoreRule);
        combined.AddRange(_add);
        combined.AddRange(_remove);
        combined.AddRange(_lock);
        _compiledRules = combined;
    }

    public async Task<Result> InitializeAsync()
    {
        _ignoreFileMatcher = await BuildIgnoreFileMatcherAsync(_project, _resourcesSection, _fileSystem);
        return Result.Ok();
    }

    public Result Evaluate(ResourceKey resource, ResourceAction action, bool isFolder = false)
    {
        // The engine governs project: resources today. Virtual roots (temp:, logs:)
        // and future remote roots are governed by their root capabilities and the
        // root-level system rules. Anything outside project: is allowed straight
        // through.
        if (resource.Root != ResourceKey.DefaultRoot)
        {
            return Result.Ok();
        }

        var path = resource.Path;
        if (string.IsNullOrEmpty(path))
        {
            return Result.Ok();
        }

        // System deny rules are non-overridable and apply to both files and
        // folders regardless of the isFolder hint; the .celbridge and .git
        // folders must always be inaccessible.
        foreach (var rule in _systemDeny)
        {
            if ((rule.GatedActions & action) != action)
            {
                continue;
            }
            if (rule.Matcher!.IsMatch(path, isFolder: true)
                || rule.Matcher!.IsMatch(path, isFolder: false))
            {
                return Fail(resource, action, rule);
            }
        }

        foreach (var rule in _systemAllow)
        {
            if ((rule.GatedActions & action) != action)
            {
                continue;
            }
            if (rule.Matcher!.IsMatch(path, isFolder: isFolder))
            {
                return Result.Ok();
            }
        }

        if (action == ResourceAction.Write)
        {
            foreach (var rule in _lock)
            {
                if (rule.Matcher!.IsMatch(path, isFolder: isFolder))
                {
                    return Fail(resource, action, rule);
                }
            }

            return Result.Ok();
        }

        // List and Read share the set model: remove beats add beats the ignore
        // baseline.
        foreach (var rule in _remove)
        {
            if (rule.Matcher!.IsMatch(path, isFolder: isFolder))
            {
                return Fail(resource, action, rule);
            }
        }

        foreach (var rule in _add)
        {
            if (rule.Matcher!.IsMatch(path, isFolder: isFolder))
            {
                return Result.Ok();
            }
        }

        if (!_ignoreFileMatcher.IsIgnored(path, isFolder))
        {
            return Result.Ok();
        }

        // The path is ignored and not added back. A folder that an add pattern
        // can reach below it must still be listable so the registry walk descends
        // to the add target, even though the folder itself is otherwise ignored.
        if (action == ResourceAction.List
            && isFolder
            && IsAddReachable(path))
        {
            return Result.Ok();
        }

        return Fail(resource, action, _ignoreRule);
    }

    private bool IsAddReachable(string folderPath)
    {
        foreach (var prefix in _addPrefixes)
        {
            if (prefix.Length == 0)
            {
                // Bare-name add pattern matches at any depth, so any folder may
                // contain a match.
                return true;
            }
            if (string.Equals(prefix, folderPath, StringComparison.Ordinal))
            {
                return true;
            }
            // The add target sits below this folder, so the walk must descend.
            if (prefix.StartsWith(folderPath + "/", StringComparison.Ordinal))
            {
                return true;
            }
            // This folder sits at or below the add prefix, so a deeper wildcard
            // in the pattern may match the folder's descendants.
            if (folderPath.StartsWith(prefix + "/", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static Result Fail(ResourceKey resource, ResourceAction action, IPolicyRule rule)
    {
        var error = new PolicyDenialError(resource, action, rule);
        return Result.Fail(error.Message).WithException(error);
    }

    private static List<CompiledPolicyRule> BuildSystemDenyRules(bool hideProjectFile)
    {
        var rules = new List<CompiledPolicyRule>();

        // On a healthy load the project file is edited only through the Project Settings document and
        // the config commands, so it is hidden from the resource tree (List denied) while staying
        // readable and writable through the resource layer. A faulted load skips this rule so the file
        // reappears in the tree for the code editor to repair. System deny is non-overridable and is
        // evaluated before the *.celbridge system-allow, so List is denied while Read and Write pass.
        if (hideProjectFile)
        {
            rules.Add(new CompiledPolicyRule(
                source: PolicyRuleSource.SystemDeny,
                pattern: "*.celbridge",
                gatedActions: ResourceAction.List,
                description: "The Celbridge project file is edited through Project Settings, not as a tree resource.",
                matcher: ResourcePathMatcher.Compile("*.celbridge")));
        }

        // The hidden project metadata folder is invisible to every consumer
        // by design. Reads of files under it must use ILocalFileSystem with
        // raw paths.
        rules.Add(new CompiledPolicyRule(
            source: PolicyRuleSource.SystemDeny,
            pattern: ".celbridge",
            gatedActions: ResourceAction.Read | ResourceAction.Write | ResourceAction.List,
            description: "The project metadata folder is reserved by Celbridge and cannot be addressed as a resource.",
            matcher: ResourcePathMatcher.Compile(".celbridge")));

        rules.Add(new CompiledPolicyRule(
            source: PolicyRuleSource.SystemDeny,
            pattern: ".celbridge/**",
            gatedActions: ResourceAction.Read | ResourceAction.Write | ResourceAction.List,
            description: "Files under the project metadata folder are reserved by Celbridge.",
            matcher: ResourcePathMatcher.Compile(".celbridge/**")));

        // Legacy visible metadata folder, retained pending entity-service migration.
        rules.Add(new CompiledPolicyRule(
            source: PolicyRuleSource.SystemDeny,
            pattern: "celbridge",
            gatedActions: ResourceAction.Read | ResourceAction.Write | ResourceAction.List,
            description: "The legacy 'celbridge' metadata folder is reserved by Celbridge.",
            matcher: ResourcePathMatcher.Compile("celbridge")));

        rules.Add(new CompiledPolicyRule(
            source: PolicyRuleSource.SystemDeny,
            pattern: "celbridge/**",
            gatedActions: ResourceAction.Read | ResourceAction.Write | ResourceAction.List,
            description: "Files under the legacy 'celbridge' metadata folder are reserved by Celbridge.",
            matcher: ResourcePathMatcher.Compile("celbridge/**")));

        // The Git metadata folder is never listed in a .gitignore, so it is a
        // system-deny rather than a built-in ignore entry.
        rules.Add(new CompiledPolicyRule(
            source: PolicyRuleSource.SystemDeny,
            pattern: ".git",
            gatedActions: ResourceAction.Read | ResourceAction.Write | ResourceAction.List,
            description: "The Git metadata folder is reserved and cannot be addressed as a resource.",
            matcher: ResourcePathMatcher.Compile(".git")));

        rules.Add(new CompiledPolicyRule(
            source: PolicyRuleSource.SystemDeny,
            pattern: ".git/**",
            gatedActions: ResourceAction.Read | ResourceAction.Write | ResourceAction.List,
            description: "Files under the Git metadata folder are reserved.",
            matcher: ResourcePathMatcher.Compile(".git/**")));

        return rules;
    }

    private static List<CompiledPolicyRule> BuildSystemAllowRules()
    {
        var rules = new List<CompiledPolicyRule>();

        // The user-facing project file is always List and Write allowed so a
        // restrictive [resources] configuration cannot brick the in-app editor.
        rules.Add(new CompiledPolicyRule(
            source: PolicyRuleSource.SystemAllow,
            pattern: "*.celbridge",
            gatedActions: ResourceAction.Read | ResourceAction.Write | ResourceAction.List,
            description: "The Celbridge project file is always visible and writable.",
            matcher: ResourcePathMatcher.Compile("*.celbridge")));

        rules.Add(new CompiledPolicyRule(
            source: PolicyRuleSource.SystemAllow,
            pattern: "package.toml",
            gatedActions: ResourceAction.Read | ResourceAction.Write | ResourceAction.List,
            description: "The package manifest is always visible and writable.",
            matcher: ResourcePathMatcher.Compile("package.toml")));

        rules.Add(new CompiledPolicyRule(
            source: PolicyRuleSource.SystemAllow,
            pattern: "editor.toml",
            gatedActions: ResourceAction.Read | ResourceAction.Write | ResourceAction.List,
            description: "The editor manifest is always visible and writable.",
            matcher: ResourcePathMatcher.Compile("editor.toml")));

        return rules;
    }

    private static async Task<IIgnoreFileMatcher> BuildIgnoreFileMatcherAsync(
        IProject? project,
        ResourcesSection resourcesSection,
        ILocalFileSystem fileSystem)
    {
        // An empty ignore-file name disables the baseline; with no live project
        // there is no folder to resolve the file against.
        if (string.IsNullOrEmpty(resourcesSection.IgnoreFile)
            || project is null)
        {
            return new IgnoreFileMatcher(Array.Empty<string>());
        }

        var ignoreFilePath = Path.Combine(project.ProjectFolderPath, resourcesSection.IgnoreFile);
        var readResult = await fileSystem.ReadAllTextAsync(ignoreFilePath);
        if (readResult.IsFailure)
        {
            // A missing ignore-file means an empty ignore set, not a fallback to
            // built-in defaults.
            return new IgnoreFileMatcher(Array.Empty<string>());
        }

        var content = readResult.Value;
        var lines = content.Replace("\r", string.Empty).Split('\n');
        return new IgnoreFileMatcher(lines);
    }

    // Computes the static leading path of each add pattern (the segments before
    // the first wildcard), used to bound the registry walk into ignored folders.
    // A bare-name pattern that matches at any depth is recorded as an empty
    // string.
    private static IReadOnlyList<string> BuildAddPrefixes(IReadOnlyList<string> addPatterns)
    {
        var prefixes = new List<string>();
        foreach (var pattern in addPatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            var trimmed = pattern.TrimEnd('/');
            if (!trimmed.Contains('/'))
            {
                prefixes.Add(string.Empty);
                continue;
            }

            var segments = trimmed.Split('/');
            var prefixSegments = new List<string>();
            foreach (var segment in segments)
            {
                if (segment.Contains('*'))
                {
                    break;
                }
                prefixSegments.Add(segment);
            }
            prefixes.Add(string.Join("/", prefixSegments));
        }
        return prefixes;
    }

    private static List<CompiledPolicyRule> CompileProjectRules(
        IReadOnlyList<string> patterns,
        PolicyRuleSource source,
        ResourceAction gatedActions,
        string description)
    {
        var rules = new List<CompiledPolicyRule>();
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            rules.Add(new CompiledPolicyRule(
                source: source,
                pattern: pattern,
                gatedActions: gatedActions,
                description: description,
                matcher: ResourcePathMatcher.Compile(pattern)));
        }
        return rules;
    }
}

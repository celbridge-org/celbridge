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

    public ResourcePathMatcher Matcher { get; }

    public CompiledPolicyRule(
        PolicyRuleSource source,
        string pattern,
        ResourceAction gatedActions,
        string description,
        ResourcePathMatcher matcher)
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
/// (system rules, project [resources] rules, built-in default excludes) and
/// evaluates against the compiled matchers per call.
/// </summary>
public sealed class ResourcePolicy : IResourcePolicy
{
    private readonly List<CompiledPolicyRule> _systemDeny;
    private readonly List<CompiledPolicyRule> _systemAllow;
    private readonly List<CompiledPolicyRule> _projectInclude;
    private readonly List<CompiledPolicyRule> _projectExclude;
    private readonly List<CompiledPolicyRule> _builtinExclude;
    private readonly List<CompiledPolicyRule> _projectLocked;

    private readonly IReadOnlyList<IPolicyRule> _compiledRules;

    public IReadOnlyList<IPolicyRule> CompiledRules => _compiledRules;

    public ResourcePolicy(IProjectService projectService)
    {
        var project = projectService.CurrentProject;
        var resourcesSection = project?.Config.Resources ?? new ResourcesSection();

        _systemDeny = BuildSystemDenyRules();
        _systemAllow = BuildSystemAllowRules();
        _builtinExclude = BuildBuiltinExcludeRules(resourcesSection.Include);
        _projectInclude = CompileProjectRules(
            resourcesSection.Include,
            PolicyRuleSource.ProjectInclude,
            ResourceAction.List | ResourceAction.Read | ResourceAction.Write,
            "Pattern from the project '[resources].include' list.");
        _projectExclude = CompileProjectRules(
            resourcesSection.Exclude,
            PolicyRuleSource.ProjectExclude,
            ResourceAction.List | ResourceAction.Read | ResourceAction.Write,
            "Pattern from the project '[resources].exclude' list.");
        _projectLocked = CompileProjectRules(
            resourcesSection.Locked,
            PolicyRuleSource.ProjectLocked,
            ResourceAction.Write,
            "Pattern from the project '[resources].locked' list. The resource is frozen in place.");

        var combined = new List<IPolicyRule>();
        combined.AddRange(_systemDeny);
        combined.AddRange(_systemAllow);
        combined.AddRange(_projectInclude);
        combined.AddRange(_projectExclude);
        combined.AddRange(_builtinExclude);
        combined.AddRange(_projectLocked);
        _compiledRules = combined;
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
        // folders regardless of the isFolder hint; the .celbridge folder must
        // always be inaccessible.
        foreach (var rule in _systemDeny)
        {
            if ((rule.GatedActions & action) != action)
            {
                continue;
            }
            if (rule.Matcher.IsMatch(path, isFolder: true)
                || rule.Matcher.IsMatch(path, isFolder: false))
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
            if (rule.Matcher.IsMatch(path, isFolder: isFolder))
            {
                return Result.Ok();
            }
        }

        if (action == ResourceAction.Write)
        {
            foreach (var rule in _projectLocked)
            {
                if (rule.Matcher.IsMatch(path, isFolder: isFolder))
                {
                    return Fail(resource, action, rule);
                }
            }

            return Result.Ok();
        }

        // List and Read share the same include/exclude/builtin pipeline:
        // matched-include → not-matched-exclude → not-matched-builtin → allow.
        if (_projectInclude.Count == 0)
        {
            return BuildIncludeFailure(resource, action);
        }

        bool includedByAny = false;
        foreach (var rule in _projectInclude)
        {
            if (rule.Matcher.IsMatch(path, isFolder: isFolder))
            {
                includedByAny = true;
                break;
            }
        }
        if (!includedByAny)
        {
            return BuildIncludeFailure(resource, action);
        }

        foreach (var rule in _projectExclude)
        {
            if (rule.Matcher.IsMatch(path, isFolder: isFolder))
            {
                return Fail(resource, action, rule);
            }
        }

        foreach (var rule in _builtinExclude)
        {
            if (rule.Matcher.IsMatch(path, isFolder: isFolder))
            {
                return Fail(resource, action, rule);
            }
        }

        return Result.Ok();
    }

    private Result BuildIncludeFailure(ResourceKey resource, ResourceAction action)
    {
        var rule = new CompiledPolicyRule(
            PolicyRuleSource.ProjectInclude,
            pattern: "(no match)",
            gatedActions: ResourceAction.List | ResourceAction.Read,
            description: "The resource is not covered by any '[resources].include' pattern. Add a pattern or '*' to make it visible.",
            matcher: ResourcePathMatcher.Compile("*"));
        return Fail(resource, action, rule);
    }

    private static Result Fail(ResourceKey resource, ResourceAction action, IPolicyRule rule)
    {
        var error = new PolicyDenialError(resource, action, rule);
        return Result.Fail(error.Message).WithException(error);
    }

    private static List<CompiledPolicyRule> BuildSystemDenyRules()
    {
        var rules = new List<CompiledPolicyRule>();

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
            pattern: "document.toml",
            gatedActions: ResourceAction.Read | ResourceAction.Write | ResourceAction.List,
            description: "The document manifest is always visible and writable.",
            matcher: ResourcePathMatcher.Compile("document.toml")));

        return rules;
    }

    // Each tuple is (pattern, description). Compiled into the rule list with a
    // suppression check against the project's include list per the design's
    // built-in override mechanism (a literal pattern in include suppresses the
    // matching built-in). Bare-name patterns match any path segment, so the
    // folder name alone catches everything under that folder.
    private static readonly (string Pattern, string Description)[] BuiltinExcludePatterns = new[]
    {
        (".*",           "Leading-dot files and folders are hidden by default (covers .git, .vscode, .env, .DS_Store)."),
        ("__pycache__",  "Python cache folder."),
        ("*.pyc",        "Compiled Python bytecode."),
        ("*.pyo",        "Optimised Python bytecode."),
        ("*.pyd",        "Python dynamic module."),
        ("node_modules", "Node package install folder."),
        ("Python/Lib",   "Python virtual environment library folder."),
        ("*.tmp",        "Temporary file."),
        ("*.bak",        "Backup file."),
        ("*.swp",        "Vim swap file."),
        ("*.swo",        "Vim swap file."),
        ("*.swn",        "Vim swap file."),
        ("*.crdownload", "Browser in-progress download."),
        ("*.part",       "Browser in-progress download."),
        ("~$*",          "Office lock file."),
        ("~WRL*",        "Word temporary file."),
        (".vs",          "Visual Studio cache folder."),
        ("bin",          "Build output folder."),
        ("obj",          "Build intermediate folder."),
        ("Thumbs.db",    "Windows thumbnail cache file."),
        ("desktop.ini",  "Windows folder settings file."),
        (".DS_Store",    "macOS folder metadata file."),
    };

    private static List<CompiledPolicyRule> BuildBuiltinExcludeRules(IReadOnlyList<string> includePatterns)
    {
        var rules = new List<CompiledPolicyRule>();

        foreach (var (pattern, description) in BuiltinExcludePatterns)
        {
            if (IsSuppressedByInclude(pattern, includePatterns))
            {
                continue;
            }

            rules.Add(new CompiledPolicyRule(
                source: PolicyRuleSource.BuiltinExclude,
                pattern: pattern,
                gatedActions: ResourceAction.List | ResourceAction.Read,
                description: description,
                matcher: ResourcePathMatcher.Compile(pattern)));
        }

        return rules;
    }

    // A built-in default-exclude is suppressed iff the user has written the
    // literal pattern into [resources].include. The wildcard "*" does not count
    // as a literal; users wanting to opt out of one entry must spell it.
    private static bool IsSuppressedByInclude(string builtinPattern, IReadOnlyList<string> includePatterns)
    {
        foreach (var includePattern in includePatterns)
        {
            if (string.Equals(includePattern, "*", StringComparison.Ordinal))
            {
                continue;
            }
            if (ResourcePathMatcher.LiteralEquivalent(includePattern, builtinPattern))
            {
                return true;
            }
        }
        return false;
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

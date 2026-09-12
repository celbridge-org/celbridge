using Celbridge.Projects;
using Celbridge.Utilities;

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
/// Workspace-scoped policy engine. Access is decided by the system-deny tier
/// alone, which no project configuration can reach. The project's 'hide' and
/// 'search-exclude' patterns are compiled alongside it and answer the two
/// ergonomic questions: what the Explorer draws, and what the indexers walk.
/// </summary>
public sealed class ResourcePolicy : IResourcePolicy
{
    private readonly List<CompiledPolicyRule> _systemDeny;

    private readonly ResourcePatternSet _hide;
    private readonly ResourcePatternSet _searchExclude;

    public ResourcePolicy(IProjectService projectService)
    {
        var resourcesSection = projectService.CurrentProject?.Config.Resources ?? new ResourcesSection();

        _systemDeny = BuildSystemDenyRules();
        _hide = ResourcePatternSet.Compile(resourcesSection.Hide);
        _searchExclude = ResourcePatternSet.Compile(resourcesSection.SearchExclude);
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

        // A reserved name is reserved whatever kind of entry sits at that path.
        // CompileReservedMatcher refuses a folders-only pattern, so the caller's hint
        // cannot change a system verdict.
        foreach (var rule in _systemDeny)
        {
            if ((rule.GatedActions & action) != action)
            {
                continue;
            }
            if (rule.Matcher.IsMatch(path, isFolder))
            {
                var error = new PolicyDenialError(resource, action, rule);
                return Result.Fail(error.Message).WithException(error);
            }
        }

        return Result.Ok();
    }

    public bool IsHidden(ResourceKey resource, bool isFolder)
    {
        return MatchesProjectPatterns(_hide, resource, isFolder);
    }

    public bool IsSearchExcluded(ResourceKey resource, bool isFolder)
    {
        return MatchesProjectPatterns(_searchExclude, resource, isFolder);
    }

    // The configured patterns are written against project paths, so a resource
    // under any other root is never a match.
    private static bool MatchesProjectPatterns(ResourcePatternSet patterns, ResourceKey resource, bool isFolder)
    {
        if (resource.Root != ResourceKey.DefaultRoot)
        {
            return false;
        }

        return patterns.IsMatch(resource.Path, isFolder);
    }

    // A system rule reserves a name rather than a kind of entry, so a folders-only
    // pattern would be written and then ignored at evaluation. The rule set is
    // hardcoded, so this rejects a mistake in this file rather than any user input.
    internal static ResourcePathMatcher CompileReservedMatcher(string pattern)
    {
        var matcher = ResourcePathMatcher.Compile(pattern);
        if (matcher.Target == PathMatchTarget.FoldersOnly)
        {
            throw new ArgumentException(
                $"A system deny pattern cannot be folders-only: '{pattern}'. Drop the trailing slash, which a system rule does not honour.",
                nameof(pattern));
        }

        return matcher;
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
            gatedActions: ResourceAction.Read | ResourceAction.Write,
            description: "The project metadata folder is reserved by Celbridge and cannot be addressed as a resource.",
            matcher: CompileReservedMatcher(".celbridge")));

        rules.Add(new CompiledPolicyRule(
            source: PolicyRuleSource.SystemDeny,
            pattern: ".celbridge/**",
            gatedActions: ResourceAction.Read | ResourceAction.Write,
            description: "Files under the project metadata folder are reserved by Celbridge.",
            matcher: CompileReservedMatcher(".celbridge/**")));

        rules.Add(new CompiledPolicyRule(
            source: PolicyRuleSource.SystemDeny,
            pattern: ".git",
            gatedActions: ResourceAction.Read | ResourceAction.Write,
            description: "The Git metadata folder is reserved and cannot be addressed as a resource.",
            matcher: CompileReservedMatcher(".git")));

        rules.Add(new CompiledPolicyRule(
            source: PolicyRuleSource.SystemDeny,
            pattern: ".git/**",
            gatedActions: ResourceAction.Read | ResourceAction.Write,
            description: "Files under the Git metadata folder are reserved.",
            matcher: CompileReservedMatcher(".git/**")));

        return rules;
    }
}

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

    public IPathMatcher Matcher { get; }

    public CompiledPolicyRule(
        PolicyRuleSource source,
        string pattern,
        ResourceAction gatedActions,
        string description,
        IPathMatcher matcher)
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

    private readonly GitignoreMatcher _hide;
    private readonly GitignoreMatcher _searchExclude;

    public ResourcePolicy(IProjectService projectService)
    {
        var resourcesSection = projectService.CurrentProject?.Config.Resources ?? new ResourcesSection();

        _systemDeny = BuildSystemDenyRules();
        _hide = CompileList(resourcesSection.Hide);
        _searchExclude = CompileList(resourcesSection.SearchExclude);
        SearchScope = new SearchScopeMatcher(_searchExclude);
    }

    public IPathMatcher SearchScope { get; }

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

        // A reserved name is reserved whatever kind of entry sits at that path. Every
        // system rule matches files and folders alike, so the caller's hint leaves the
        // verdict unchanged.
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
    private static bool MatchesProjectPatterns(GitignoreMatcher patterns, ResourceKey resource, bool isFolder)
    {
        if (resource.Root != ResourceKey.DefaultRoot)
        {
            return false;
        }

        return patterns.IsMatch(resource.Path, isFolder);
    }

    // The project file's parser drops each refused line, so a list read from a project compiles. A list that fails to
    // compile matches nothing.
    private static GitignoreMatcher CompileList(IReadOnlyList<string> lines)
    {
        var compileResult = GitignoreMatcher.Compile(lines, ignoreCase: false);
        if (compileResult.IsFailure)
        {
            return GitignoreMatcher.Compile([], ignoreCase: false).Value;
        }

        return compileResult.Value;
    }

    private static List<CompiledPolicyRule> BuildSystemDenyRules()
    {
        var rules = new List<CompiledPolicyRule>();

        rules.Add(new CompiledPolicyRule(
            source: PolicyRuleSource.SystemDeny,
            pattern: GitFolderRule.Pattern,
            gatedActions: ResourceAction.Read | ResourceAction.Write,
            description: "The Git metadata folder is reserved and cannot be addressed as a resource.",
            matcher: GitFolderRule.Matcher));

        return rules;
    }
}

namespace Celbridge.Resources;

/// <summary>
/// The actions a caller may attempt against a resource. The policy engine
/// evaluates one action per call. List gates whether a resource is visible in
/// the registry and to enumerations; Read gates content access; Write gates
/// every mutating operation.
/// </summary>
[Flags]
public enum ResourceAction
{
    None  = 0,
    Read  = 1 << 0,
    Write = 1 << 1,
    List  = 1 << 2,
}

/// <summary>
/// The provenance of a rule matched by the policy engine. Determines
/// precedence during evaluation and the wording of the user-facing denial.
/// </summary>
public enum PolicyRuleSource
{
    /// <summary>
    /// Hard-coded non-overridable deny rule (e.g. ".celbridge/" project metadata folder).
    /// </summary>
    SystemDeny,

    /// <summary>
    /// Hard-coded non-overridable allow rule (e.g. "*.celbridge", "package.toml",
    /// "document.toml" — protects the in-app editor from a user-written lockdown).
    /// </summary>
    SystemAllow,

    /// <summary>
    /// Match against the project's ignore-file (gitignore-format). A path the
    /// ignore-file matches is not a resource unless an Add pattern brings it
    /// back: it is invisible to enumeration, watcher events drop, and reads
    /// resolve to "no such resource".
    /// </summary>
    IgnoreFile,

    /// <summary>
    /// Match against [resources].add. Brings a path into the resource set even
    /// when the ignore-file hides it.
    /// </summary>
    ProjectAdd,

    /// <summary>
    /// Match against [resources].remove. Drops a path from the resource set.
    /// Takes precedence over Add and the ignore baseline.
    /// </summary>
    ProjectRemove,

    /// <summary>
    /// Match against [resources].lock. Gates every structural change (content
    /// write, delete, move, rename) on the resource and freezes its path so no
    /// ancestor folder can be moved, renamed, or deleted. The resource stays
    /// visible and readable.
    /// </summary>
    ProjectLocked,
}

/// <summary>
/// A single rule contributed by one of the policy rule sources. Exposed by
/// the engine through PolicyDenialError so consumers can format actionable
/// error text.
/// </summary>
public interface IPolicyRule
{
    /// <summary>
    /// The source of this rule. Drives precedence and error wording.
    /// </summary>
    PolicyRuleSource Source { get; }

    /// <summary>
    /// The pattern as the user wrote it (e.g. "assets/**") or a synthetic
    /// literal for system rules (e.g. ".celbridge/").
    /// </summary>
    string Pattern { get; }

    /// <summary>
    /// Which actions this rule gates. A single rule may gate multiple actions:
    /// the .celbridge/ system-deny rule denies Read, Write, and List together.
    /// </summary>
    ResourceAction GatedActions { get; }

    /// <summary>
    /// Human-readable description used to format the user-facing denial text.
    /// </summary>
    string Description { get; }
}

/// <summary>
/// Carrier attached to a Result.Fail when an operation is denied by the policy
/// engine. Records the resource key, the attempted action, and the matched
/// rule so callers can format actionable error text without re-parsing the
/// message string. Attach to a failure via Result.WithException; detect with
/// HasException<PolicyDenialError>.
/// </summary>
public sealed class PolicyDenialError : Exception
{
    /// <summary>
    /// The resource whose access was denied.
    /// </summary>
    public ResourceKey Resource { get; }

    /// <summary>
    /// The action that was attempted on the resource.
    /// </summary>
    public ResourceAction Action { get; }

    /// <summary>
    /// The rule that matched the resource and gated the action.
    /// </summary>
    public IPolicyRule MatchedRule { get; }

    public PolicyDenialError(ResourceKey resource, ResourceAction action, IPolicyRule matchedRule)
        : base(FormatMessage(resource, action, matchedRule))
    {
        Resource = resource;
        Action = action;
        MatchedRule = matchedRule;
    }

    private static string FormatMessage(ResourceKey resource, ResourceAction action, IPolicyRule rule)
    {
        var actionText = action switch
        {
            ResourceAction.Read => "Read",
            ResourceAction.Write => "Write",
            ResourceAction.List => "List",
            _ => action.ToString(),
        };

        var sourceText = rule.Source switch
        {
            PolicyRuleSource.SystemDeny => "system policy",
            PolicyRuleSource.SystemAllow => "system policy",
            PolicyRuleSource.IgnoreFile => "[resources].ignore-file",
            PolicyRuleSource.ProjectAdd => "[resources].add",
            PolicyRuleSource.ProjectRemove => "[resources].remove",
            PolicyRuleSource.ProjectLocked => "[resources].lock",
            _ => rule.Source.ToString(),
        };

        return $"{actionText} of '{resource}' was denied by the {sourceText} pattern '{rule.Pattern}'. {rule.Description}";
    }
}

/// <summary>
/// The single source of truth for "is this (ResourceKey, action) allowed".
/// Workspace-scoped: each workspace owns its own engine reflecting that
/// project's [resources] configuration plus the built-in default-excludes.
/// </summary>
public interface IResourcePolicy
{
    /// <summary>
    /// Returns Result.Ok on allow, Result.Fail on deny. The failure carries a
    /// PolicyDenialError describing the matched rule via WithException(); the
    /// FirstErrorMessage is the formatted denial text. The isFolder hint lets
    /// folder-only patterns (those ending with '/') exclude file paths of the
    /// same name; pass true when the caller knows the resource is a folder.
    /// </summary>
    Result Evaluate(ResourceKey resource, ResourceAction action, bool isFolder = false);

    /// <summary>
    /// Snapshot of the rules currently compiled into the engine, in evaluation
    /// order. Used by config_guide and diagnostic logging to surface the
    /// active rule set without re-parsing configuration.
    /// </summary>
    IReadOnlyList<IPolicyRule> CompiledRules { get; }
}

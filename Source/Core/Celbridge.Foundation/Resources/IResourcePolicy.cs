namespace Celbridge.Resources;

/// <summary>
/// The actions a caller may attempt against a resource. The policy engine
/// evaluates one action per call. Read gates content access. Write gates every
/// mutating operation.
/// </summary>
[Flags]
public enum ResourceAction
{
    None  = 0,
    Read  = 1 << 0,
    Write = 1 << 1,
}

/// <summary>
/// The provenance of a rule matched by the policy engine. Determines the
/// wording of the user-facing denial.
/// </summary>
public enum PolicyRuleSource
{
    /// <summary>
    /// Hard-coded non-overridable deny rule (e.g. ".celbridge/" project metadata folder).
    /// </summary>
    SystemDeny,
}

/// <summary>
/// A single rule contributed by one of the policy rule sources. Exposed by
/// the engine through PolicyDenialError so consumers can format actionable
/// error text.
/// </summary>
public interface IPolicyRule
{
    /// <summary>
    /// The source of this rule. Drives the error wording.
    /// </summary>
    PolicyRuleSource Source { get; }

    /// <summary>
    /// The pattern the rule matches on, as a synthetic literal for system rules
    /// (e.g. ".celbridge/").
    /// </summary>
    string Pattern { get; }

    /// <summary>
    /// Which actions this rule gates. A single rule may gate multiple actions:
    /// the .celbridge/ system-deny rule denies Read and Write together.
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
/// HasException&lt;PolicyDenialError&gt;.
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
            _ => action.ToString(),
        };

        var sourceText = rule.Source switch
        {
            PolicyRuleSource.SystemDeny => "system policy",
            _ => rule.Source.ToString(),
        };

        return $"{actionText} of '{resource}' was denied by the {sourceText} pattern '{rule.Pattern}'. {rule.Description}";
    }
}

/// <summary>
/// The compiled view of a project's [celbridge.resources] settings, plus the
/// non-configurable access invariants. Workspace-scoped: each workspace owns
/// its own engine reflecting that project's configuration.
/// </summary>
public interface IResourcePolicy
{
    /// <summary>
    /// Returns Result.Ok on allow, Result.Fail on deny. The failure carries a
    /// PolicyDenialError describing the matched rule via WithException(); the
    /// FirstErrorMessage is the formatted denial text. Access is governed by the
    /// invariants alone: nothing a project configures can deny a read or a write.
    /// </summary>
    Result Evaluate(ResourceKey resource, ResourceAction action, bool isFolder = false);

    /// <summary>
    /// Whether the project's 'hide' patterns match the resource. Cosmetic and
    /// binding on the Explorer only: a hidden resource stays readable and
    /// writable by tools and the editor.
    /// </summary>
    bool IsHidden(ResourceKey resource, bool isFolder);

    /// <summary>
    /// Whether the project's 'search-exclude' patterns match the resource. Binds
    /// search, reference scanning and tag queries, and until the resource index
    /// lands the project tree walk as well. An excluded resource stays readable
    /// and writable.
    /// </summary>
    bool IsSearchExcluded(ResourceKey resource, bool isFolder);
}

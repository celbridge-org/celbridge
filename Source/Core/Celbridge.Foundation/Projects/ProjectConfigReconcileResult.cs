using Celbridge.Packages;

namespace Celbridge.Projects;

/// <summary>
/// A contribution resolved as active by the reconcile pass: the discovered contribution and the raw,
/// validated config overrides to apply to it. The registry turns this into the runtime editor.
/// </summary>
public sealed record ResolvedContribution(
    EditorContribution Contribution,
    IReadOnlyDictionary<string, object?> Config);

/// <summary>
/// The result of reconciling a parsed project config against the discovered contributions: the
/// normalized config to persist (overrides only), the resolved active set for the runtime, and the
/// warnings raised while dropping unknown, orphaned, or redundant entries.
/// </summary>
public sealed record ProjectConfigReconcileResult(
    ProjectConfig Config,
    IReadOnlyList<ResolvedContribution> ActiveContributions,
    IReadOnlyList<string> Warnings);

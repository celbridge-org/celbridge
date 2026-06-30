namespace Celbridge;

/// <summary>
/// A configuration overlay that sets macOS-specific feature flag values while the macOS port is in
/// progress. It is layered over appsettings.json so the macOS desktop head runs with unported
/// subsystems gated off, while Windows is unaffected.
/// </summary>
internal static class MacOSFeatureFlagOverlay
{
    public static IReadOnlyDictionary<string, string?> Overrides { get; } = new Dictionary<string, string?>
    {
        // No flag overrides remain. Other unported subsystems are gated by runtime platform checks,
        // not flags, so they need no entry here.
    };
}

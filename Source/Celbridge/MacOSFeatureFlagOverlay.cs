namespace Celbridge;

/// <summary>
/// A configuration overlay that sets macOS-specific feature flag values while the macOS port is in
/// progress. It is layered over appsettings.json so the macOS desktop head runs with unported
/// subsystems gated off, while Windows is unaffected.
/// </summary>
/// <remarks>
/// This is a live checklist of what is not yet ported. As each workstream lands and is verified on
/// macOS, delete its entry here so the flag defaults back on; the overlay is empty at parity with
/// Windows.
///
/// Only flag-driven gates live here. Several other macOS-unported subsystems are gated by runtime
/// platform checks elsewhere rather than by a feature flag, so they need no entry:
///   - Contribution / WebView editors: CustomDocumentViewFactory.CreateDocumentView returns
///     Result.Fail when !OperatingSystem.IsWindows() (widens to macOS once the WebView layer lands).
///   - Credential storage: DpapiCredentialProtector.IsAvailable is false off Windows, so the
///     credentials UI degrades to a "store unavailable" message.
///   - File-association activation: App.xaml.cs has a documented no-op on the non-Windows head.
/// </remarks>
internal static class MacOSFeatureFlagOverlay
{
    public static IReadOnlyDictionary<string, string?> Overrides { get; } = new Dictionary<string, string?>
    {
        // No flag overrides remain: the console panel (its terminal is WS2, its Python init is WS3) now
        // runs on macOS. Other unported subsystems are gated by runtime platform checks, not flags (see
        // the remarks above), so they need no entry here.
    };
}

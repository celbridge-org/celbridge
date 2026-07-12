namespace Celbridge.Tests.Architecture;

/// <summary>
/// Guards the central focus-tracking contract: reports into the focus arbiter (IFocusService.OnFocusReceived
/// and ClearFocus) must come only from the central focus components, so a panel or web surface that forgets
/// to declare its focus fails safe (a cleared caret) rather than silently leaving a stale one. Enforcement
/// counterpart to the PanelFocusTracker / FocusTracking.Panel / IWebViewFocusRegistry design.
/// </summary>
[TestFixture]
public class FocusContainmentTests
{
    // The only files allowed to reference the arbiter's focus-report API: the interface that declares it, the
    // arbiter that implements it, the central managed observer, and the central web-surface integration. There
    // is no separate R8 allowlist to add: the rail selection and web-surface grants both report through these.
    private static readonly HashSet<string> CentralFocusComponents = new(StringComparer.OrdinalIgnoreCase)
    {
        "IFocusService.cs",
        "FocusService.cs",
        "PanelFocusTracker.cs",
        "WebViewFocusRegistry.cs",
    };

    [Test]
    public void FocusReports_ComeOnlyFromCentralComponents()
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        Directory.Exists(sourceFolder).Should().BeTrue(
            "the repository Source folder must be locatable from the test binary");

        var offenders = new List<string>();
        foreach (var filePath in ArchitectureHelpers.EnumerateProductionSourceFiles(sourceFolder))
        {
            if (CentralFocusComponents.Contains(Path.GetFileName(filePath)))
            {
                continue;
            }

            var contents = File.ReadAllText(filePath);
            if (contents.Contains("OnFocusReceived(") ||
                contents.Contains("ClearFocus("))
            {
                offenders.Add(Path.GetRelativePath(sourceFolder, filePath));
            }
        }

        offenders.Should().BeEmpty(
            "focus reports (IFocusService.OnFocusReceived / ClearFocus) must come only from the central focus components; route new panel or web-surface focus through FocusTracking.Panel or IWebViewFocusRegistry instead");
    }
}

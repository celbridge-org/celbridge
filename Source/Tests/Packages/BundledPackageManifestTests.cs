using Celbridge.Packages;
using Celbridge.Tests.Architecture;

namespace Celbridge.Tests.Packages;

/// <summary>
/// The bundled editor packages ship with the app and are activated at every project load, so a
/// manifest the loader rejects is a missing editor at runtime rather than a build failure. These
/// tests load the real manifests from the source tree through the real loader.
/// </summary>
[TestFixture]
public class BundledPackageManifestTests
{
    [Test]
    public void EveryBundledPackageManifestLoads()
    {
        var manifestPaths = FindBundledPackageManifests();

        // A change to the folder layout that stops the manifests being found must fail loudly rather
        // than pass with nothing checked.
        manifestPaths.Should().NotBeEmpty("bundled editor packages live under Modules/**/Editors");

        // The code editor claims its file types from the host's language catalog rather than listing
        // them, so the loader needs a catalog to resolve that entry against.
        var fileTypeCatalog = Substitute.For<IFileTypeCatalog>();
        fileTypeCatalog.LanguageExtensions.Returns(new[] { ".cs", ".js" });

        var failures = new List<string>();

        foreach (var manifestPath in manifestPaths)
        {
            var loadResult = PackageManifestLoader.LoadPackage(manifestPath, fileTypeCatalog: fileTypeCatalog);
            if (loadResult.IsFailure)
            {
                failures.Add($"{manifestPath}: {loadResult.FirstErrorMessage}");
            }
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    [Test]
    public void TheReportEditorClaimsTheReportExtension()
    {
        var manifestPath = Path.Combine(
            ArchitectureHelpers.FindSourceFolder(),
            "Modules",
            "Celbridge.DocumentEditors",
            "Editors",
            "Report",
            "package.toml");

        var loadResult = PackageManifestLoader.LoadPackage(manifestPath);
        loadResult.IsSuccess.Should().BeTrue(loadResult.FirstErrorMessage);

        var package = loadResult.Value;
        var editor = package.Editors.Should().ContainSingle().Subject;

        editor.Id.Should().Be("report");
        editor.FileTypes.Should().ContainSingle().Which.FileExtension.Should().Be(".report");

        // The editor navigates through these two tools, and an undeclared tool is absent from the
        // client's proxy rather than failing at the call site.
        package.Info.PermittedTools.Should().Contain("document.open");
        package.Info.PermittedTools.Should().Contain("explorer.select");
    }

    private static List<string> FindBundledPackageManifests()
    {
        var modulesFolder = Path.Combine(ArchitectureHelpers.FindSourceFolder(), "Modules");

        var manifestPaths = new List<string>();

        foreach (var editorsFolder in Directory.GetDirectories(modulesFolder, "Editors", SearchOption.AllDirectories))
        {
            // Build output holds copies of the same manifests, which would report every failure twice.
            if (IsBuildOutput(editorsFolder))
            {
                continue;
            }

            foreach (var packageFolder in Directory.GetDirectories(editorsFolder))
            {
                var manifestPath = Path.Combine(packageFolder, "package.toml");
                if (File.Exists(manifestPath))
                {
                    manifestPaths.Add(manifestPath);
                }
            }
        }

        return manifestPaths;
    }

    private static bool IsBuildOutput(string folderPath)
    {
        var segments = folderPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }
}

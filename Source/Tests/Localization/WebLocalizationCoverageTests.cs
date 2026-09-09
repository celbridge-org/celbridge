using System.Text.Json;
using System.Text.RegularExpressions;

namespace Celbridge.Tests.Localization;

/// <summary>
/// Each WebView editor localizes its UI at runtime from localization/{locale}.json, keyed by data-loc-key
/// / data-loc-title in the markup it renders. A key with no en.json entry renders as the raw key name in the
/// UI (plus a console warning). This test asserts every such key resolves, for every web app copied into the
/// test output, so a missing entry fails the build instead of shipping a visible gap.
/// </summary>
[TestFixture]
public class WebLocalizationCoverageTests
{
    private sealed record WebApp(string Name, string FolderPath, string EnJsonPath);

    private static readonly Regex LocKeyRegex =
        new("data-loc-(?:key|title)=\"([^\"]+)\"", RegexOptions.Compiled);

    // Folders holding code the app did not author. A key in any of those is not markup the editor renders.
    private static readonly string[] ExcludedFolderNames = { "lib", "node_modules", "tests" };

    [Test]
    public void EveryDataLocKeyInAWebAppsMarkup_HasAnEnJsonEntry()
    {
        var webApps = DiscoverWebApps();

        // A change to the output layout that stops the web apps being copied must fail loudly rather than
        // pass with nothing checked.
        webApps.Should().NotBeEmpty("web apps (index.html + localization/en.json) should be copied to the test output");
        webApps.Select(app => app.Name).Should().Contain("Console");

        var failures = new List<string>();

        foreach (var webApp in webApps)
        {
            var definedKeys = LoadJsonKeys(webApp.EnJsonPath);

            foreach (var markupPath in MarkupFiles(webApp.FolderPath))
            {
                var usedKeys = LocKeyRegex.Matches(File.ReadAllText(markupPath))
                    .Select(match => match.Groups[1].Value)
                    .Distinct();

                var fileName = Path.GetRelativePath(webApp.FolderPath, markupPath);

                foreach (var key in usedKeys)
                {
                    if (!definedKeys.Contains(key))
                    {
                        failures.Add($"{webApp.Name}: '{key}' used in {fileName} has no entry in localization/en.json");
                    }
                }
            }
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    // Finds every web app in the test output: a folder holding both an index.html and a localization/en.json.
    private static IReadOnlyList<WebApp> DiscoverWebApps()
    {
        var webApps = new List<WebApp>();

        foreach (var indexHtmlPath in Directory.GetFiles(AppContext.BaseDirectory, "index.html", SearchOption.AllDirectories))
        {
            var folderPath = Path.GetDirectoryName(indexHtmlPath)!;
            var enJsonPath = Path.Combine(folderPath, "localization", "en.json");
            if (File.Exists(enJsonPath))
            {
                webApps.Add(new WebApp(Path.GetFileName(folderPath), folderPath, enJsonPath));
            }
        }

        return webApps;
    }

    // The files a web app authors its markup in: the page itself, plus the modules that carry markup of
    // their own.
    private static IEnumerable<string> MarkupFiles(string folderPath)
    {
        yield return Path.Combine(folderPath, "index.html");

        foreach (var scriptPath in Directory.GetFiles(folderPath, "*.js", SearchOption.AllDirectories))
        {
            var segments = Path.GetRelativePath(folderPath, scriptPath)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!segments.Any(segment => ExcludedFolderNames.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            {
                yield return scriptPath;
            }
        }
    }

    private static HashSet<string> LoadJsonKeys(string jsonPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            keys.Add(property.Name);
        }

        return keys;
    }
}

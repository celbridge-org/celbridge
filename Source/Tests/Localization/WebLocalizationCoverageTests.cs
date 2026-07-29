using System.Text.Json;
using System.Text.RegularExpressions;

namespace Celbridge.Tests.Localization;

/// <summary>
/// Each WebView editor localizes its UI at runtime from localization/{locale}.json, keyed by data-loc-key
/// / data-loc-title in its index.html. A key with no en.json entry renders as the raw key name in the UI
/// (plus a console warning). This test asserts every such key resolves, for every web app copied into the
/// test output, so a missing entry fails the build instead of shipping a visible gap.
/// </summary>
[TestFixture]
public class WebLocalizationCoverageTests
{
    private sealed record WebApp(string Name, string IndexHtmlPath, string EnJsonPath);

    private static readonly Regex LocKeyRegex =
        new("data-loc-(?:key|title)=\"([^\"]+)\"", RegexOptions.Compiled);

    [Test]
    public void EveryDataLocKeyInIndexHtml_HasAnEnJsonEntry()
    {
        var webApps = DiscoverWebApps();

        // A change to the output layout that stops the web apps being copied must fail loudly rather than
        // pass with nothing checked.
        webApps.Should().NotBeEmpty("web apps (index.html + localization/en.json) should be copied to the test output");
        webApps.Select(app => app.Name).Should().Contain("Console");

        var failures = new List<string>();

        foreach (var webApp in webApps)
        {
            var html = File.ReadAllText(webApp.IndexHtmlPath);
            var usedKeys = LocKeyRegex.Matches(html)
                .Select(match => match.Groups[1].Value)
                .Distinct();

            var definedKeys = LoadJsonKeys(webApp.EnJsonPath);

            foreach (var key in usedKeys)
            {
                if (!definedKeys.Contains(key))
                {
                    failures.Add($"{webApp.Name}: '{key}' used in index.html has no entry in localization/en.json");
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
                webApps.Add(new WebApp(Path.GetFileName(folderPath), indexHtmlPath, enJsonPath));
            }
        }

        return webApps;
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

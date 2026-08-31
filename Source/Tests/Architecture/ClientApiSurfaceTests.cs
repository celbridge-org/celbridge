using System.Text.RegularExpressions;

namespace Celbridge.Tests.Architecture;

/// <summary>
/// Guards the client library's public JavaScript surface, the `cel.*` API that package editors are written
/// against. Packages live outside this repository, so no scan of call sites can tell whether removing a
/// method breaks one: an editor that calls a method the bundle no longer exports throws at module scope and
/// renders blank, with nothing in the host to say why. The snapshot below is the contract. Removing or
/// renaming an entry fails this test, which is the moment to ask who is calling it and to say so in the
/// release notes; adding one is a one-line update.
/// </summary>
[TestFixture]
public class ClientApiSurfaceTests
{
    // Every method a package can reach as cel.<area>.<method>, by the module that defines the area. Keep
    // each list alphabetical. `constructor` and #private members are not part of the surface.
    private static readonly Dictionary<string, string[]> PublishedClientApi = new(StringComparer.Ordinal)
    {
        ["api/dialog-api.js"] = new[]
        {
            "alert",
            "pickFile",
            "pickIcon",
            "pickImage",
            "toast"
        },
        ["api/document-api.js"] = new[]
        {
            "load",
            "notifyChanged",
            "notifyContentLoaded",
            "notifyImportComplete",
            "onExternalChange",
            "onRequestSave",
            "onRequestState",
            "onRestoreState",
            "save",
            "writeReport"
        },
        ["api/input-api.js"] = new[]
        {
            "notifyEditAvailability",
            "notifyLinkClicked",
            "notifyShortcut"
        },
        ["api/localization-api.js"] = new[]
        {
            "loadStrings",
            "onLanguageChanged"
        },
        ["api/log-api.js"] = new[]
        {
            "debug",
            "error",
            "info",
            "warn"
        },
        ["api/tools-api.js"] = new[]
        {
            "call",
            "list",
            "loadDescriptors",
            "setDescriptors"
        },

        // The state stores behind cel.appState and cel.viewState.
        ["core/state-store.js"] = new[]
        {
            "onChanged"
        }
    };

    [Test]
    public void ThePublishedClientApiIsUnchanged()
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        var clientFolder = Path.Combine(
            sourceFolder,
            "Core",
            "Celbridge.WebHost",
            "Web",
            "celbridge-client");

        Directory.Exists(clientFolder).Should().BeTrue("the client library must be locatable from the test binary");

        var differences = new List<string>();
        foreach (var (relativePath, publishedMethods) in PublishedClientApi)
        {
            var filePath = Path.Combine(clientFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(filePath))
            {
                differences.Add($"{relativePath}: the module is gone, so every method it published is gone with it");
                continue;
            }

            var exportedMethods = ReadPublicMethods(filePath);

            foreach (var method in publishedMethods.Except(exportedMethods, StringComparer.Ordinal))
            {
                differences.Add($"{relativePath}: {method} is published but no longer exported");
            }

            foreach (var method in exportedMethods.Except(publishedMethods, StringComparer.Ordinal))
            {
                differences.Add($"{relativePath}: {method} is exported but not published");
            }
        }

        differences.Sort(StringComparer.Ordinal);
        string.Join(Environment.NewLine, differences).Should().BeEmpty(
            "the cel.* surface is what package editors are written against, and a package that calls a removed method renders blank with no host-side error; update PublishedClientApi deliberately, and treat a removal as a breaking change for package authors");
    }

    // Keywords that read exactly like a method declaration once indented: `if (ready) {`, `for (...) {`.
    private static readonly HashSet<string> JavaScriptKeywords = new(StringComparer.Ordinal)
    {
        "catch",
        "constructor",
        "for",
        "if",
        "switch",
        "while"
    };

    // Methods declared directly in a class body: four-space indented, optionally async, named without a
    // leading # (which marks a private member), and opening a body rather than being a bare call.
    private static HashSet<string> ReadPublicMethods(string filePath)
    {
        var contents = File.ReadAllText(filePath);
        var methods = new HashSet<string>(StringComparer.Ordinal);
        var pattern = @"^ {4}(?:async )?([a-zA-Z][a-zA-Z0-9]*)\s*\([^)]*\)\s*\{";

        foreach (Match match in Regex.Matches(contents, pattern, RegexOptions.Multiline))
        {
            var name = match.Groups[1].Value;
            if (JavaScriptKeywords.Contains(name))
            {
                continue;
            }

            methods.Add(name);
        }

        return methods;
    }
}

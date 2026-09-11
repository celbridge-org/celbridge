using System.Text.RegularExpressions;

namespace Celbridge.Tests.Architecture;

/// <summary>
/// Guards the client library's public JavaScript surface: the `cel.*` API and the shared `ui/` modules that
/// package editors are written against. Packages live outside this repository, so no scan of call sites can
/// tell whether a removal breaks one. An editor that calls a method the bundle no longer exports, or imports
/// a module it no longer serves, throws at module scope and renders blank, with nothing in the host to say
/// why. The snapshots below are the contract. Removing or renaming an entry fails these tests, which is the
/// moment to ask who is calling it and to say so in the release notes. Adding one is a one-line update.
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
            "notifyShortcut",
            "requestEdit"
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

    // The shared UI modules a package imports directly, by the functions each one exports. These are
    // reached as `/assets/celbridge-client/ui/<module>.js` rather than through cel.*, so a module removed
    // or renamed here breaks an editor's import the same way a removed method breaks a call. Keep each
    // list alphabetical.
    private static readonly Dictionary<string, string[]> PublishedClientUiModules = new(StringComparer.Ordinal)
    {
        ["ui/card-list.js"] = new[]
        {
            "createCardList",
            "placementForPointer"
        },
        ["ui/find-bar.js"] = new[]
        {
            "createFindBar"
        },
        ["ui/icon-field.js"] = new[]
        {
            "createIconField",
            "hasIconGlyph",
            "resolveIconClass",
            "toIconClass"
        },
        ["ui/section-switcher.js"] = new[]
        {
            "attachSectionSwitcher"
        },
        ["ui/splitter.js"] = new[]
        {
            "attachSplitter"
        }
    };

    // Functions the api/ modules export to importers, alongside the classes above. A package can
    // import these by name, so a removal breaks it the same way a removed method does. Keep each
    // list alphabetical.
    private static readonly Dictionary<string, string[]> PublishedClientApiFunctions = new(StringComparer.Ordinal)
    {
        ["api/tools-api.js"] = new[]
        {
            "buildCelProxy",
            "jsonRpcCodeForCelCode"
        }
    };

    [Test]
    public void ThePublishedClientApiFunctionsAreUnchanged()
    {
        var clientFolder = FindClientFolder();

        var differences = new List<string>();
        foreach (var (relativePath, publishedFunctions) in PublishedClientApiFunctions)
        {
            var filePath = Path.Combine(clientFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(filePath))
            {
                differences.Add($"{relativePath}: the module is gone, so every function it published is gone with it");
                continue;
            }

            var exportedFunctions = ReadExportedFunctions(filePath);

            foreach (var function in publishedFunctions.Except(exportedFunctions, StringComparer.Ordinal))
            {
                differences.Add($"{relativePath}: {function} is published but no longer exported");
            }

            foreach (var function in exportedFunctions.Except(publishedFunctions, StringComparer.Ordinal))
            {
                differences.Add($"{relativePath}: {function} is exported but not published");
            }
        }

        differences.Sort(StringComparer.Ordinal);
        string.Join(Environment.NewLine, differences).Should().BeEmpty(
            "a package can import these by name, so treat a removal as a breaking change for package authors and update PublishedClientApiFunctions deliberately");
    }

    [Test]
    public void ThePublishedClientApiIsUnchanged()
    {
        var clientFolder = FindClientFolder();

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

    [Test]
    public void ThePublishedClientUiSurfaceIsUnchanged()
    {
        var clientFolder = FindClientFolder();

        var differences = new List<string>();
        foreach (var (relativePath, publishedFunctions) in PublishedClientUiModules)
        {
            var filePath = Path.Combine(clientFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(filePath))
            {
                differences.Add($"{relativePath}: the module is gone, so every import of it fails");
                continue;
            }

            var exportedFunctions = ReadExportedFunctions(filePath);

            foreach (var function in publishedFunctions.Except(exportedFunctions, StringComparer.Ordinal))
            {
                differences.Add($"{relativePath}: {function} is published but no longer exported");
            }

            foreach (var function in exportedFunctions.Except(publishedFunctions, StringComparer.Ordinal))
            {
                differences.Add($"{relativePath}: {function} is exported but not published");
            }
        }

        differences.Sort(StringComparer.Ordinal);
        string.Join(Environment.NewLine, differences).Should().BeEmpty(
            "a package imports these modules by path, so a removed module throws at import and renders the editor blank; update PublishedClientUiModules deliberately, and treat a removal as a breaking change for package authors");
    }

    private static string FindClientFolder()
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        var clientFolder = Path.Combine(
            sourceFolder,
            "Core",
            "Celbridge.WebHost",
            "Web",
            "celbridge-client");

        Directory.Exists(clientFolder).Should().BeTrue("the client library must be locatable from the test binary");

        return clientFolder;
    }

    // Functions a module publishes to importers: declared at the top level with the export keyword.
    private static HashSet<string> ReadExportedFunctions(string filePath)
    {
        var contents = File.ReadAllText(filePath);
        var functions = new HashSet<string>(StringComparer.Ordinal);
        var pattern = @"^export\s+(?:async\s+)?function\s+([a-zA-Z][a-zA-Z0-9]*)\s*\(";

        foreach (Match match in Regex.Matches(contents, pattern, RegexOptions.Multiline))
        {
            functions.Add(match.Groups[1].Value);
        }

        return functions;
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

using System.Text.RegularExpressions;
using Celbridge.DesignTokens;
using Celbridge.Tests.Architecture;

namespace Celbridge.Tests.DesignTokens;

/// <summary>
/// The design tokens are generated from one source into a XAML dictionary and a CSS sheet, so the two
/// cannot hold different values. What they can still do is drift apart from the code that uses them: a
/// token renamed or deleted in the source leaves references behind on either side, and the CSS names are
/// a contract that packages outside this repository are written against. These tests hold both ends.
/// </summary>
[TestFixture]
public class DesignTokenCoverageTests
{
    private static readonly Regex CssReferenceRegex =
        new(@"var\(\s*(--cel-[a-z0-9-]+)", RegexOptions.Compiled);

    private static readonly Regex XamlReferenceRegex =
        new(@"\{ThemeResource\s+(\w+Color)\}", RegexOptions.Compiled);

    private static readonly string[] StyleFileExtensions =
    [
        "*.css",
        "*.js",
        "*.html"
    ];

    private static readonly string[] NativeFileExtensions =
    [
        "*.xaml",
        "*.cs"
    ];

    // Tokens deliberately declared with nothing in this repository using them, each because a package
    // author still needs the name even though no host surface draws with it.
    private static readonly string[] TokensWithoutHostConsumer =
    [
        // The regular weight is the CSS default, so no host rule declares it, but a package setting a
        // weight explicitly needs a name for the other end of the pair.
        "--cel-font-weight-regular",
        // No host surface rounds a card yet. It pairs with --cel-radius-control.
        "--cel-radius-card",
        // The document floor. The host composes its own minimums from the matching values in
        // WorkspaceConstants; the web names carry the same floor for a document sizing itself against it.
        "--cel-document-min-width",
        "--cel-document-min-height",
        // The native side paints match highlighting with SearchMatchHighlightBrush. The web name carries
        // the same value for a package rendering its own results.
        "--cel-search-highlight",
        // The workspace rail drops a selected button to this tone while its panel is unfocused, drawn from
        // ButtonActiveBackgroundBrush. An editor's own rail deliberately holds its accent fill for as long as
        // the surface is open, so nothing on the web side takes the tone.
        "--cel-button-active-bg",
        // Every host command icon is the medium step. The larger name stays for a package whose surface
        // wants a more prominent glyph than the host chrome uses.
        "--cel-icon-size-large"
    ];

    // The CSS names packages outside this repository are written against. Renaming or removing one breaks
    // those packages, so it has to be a deliberate edit here rather than a side effect of a source change.
    private static readonly string[] PublishedTokenNames =
    [
        "--cel-accent",
        "--cel-accent-text",
        "--cel-button-active-bg",
        "--cel-button-hover-bg",
        "--cel-caution",
        "--cel-chrome-bg",
        "--cel-content-bg",
        "--cel-control-bg",
        "--cel-control-border",
        "--cel-control-hover-bg",
        "--cel-divider",
        "--cel-divider-subtle",
        "--cel-document-min-height",
        "--cel-document-min-width",
        "--cel-error-text",
        "--cel-expander-content-bg",
        "--cel-expander-header-bg",
        "--cel-font-mono",
        "--cel-font-size-base",
        "--cel-font-size-heading",
        "--cel-font-size-small",
        "--cel-font-ui",
        "--cel-font-weight-regular",
        "--cel-font-weight-strong",
        "--cel-icon-button-size",
        "--cel-icon-size-large",
        "--cel-icon-size-medium",
        "--cel-icon-size-small",
        "--cel-page-zoom",
        "--cel-panel-edge",
        "--cel-panel-header-height",
        "--cel-panel-inset",
        "--cel-radius-button",
        "--cel-radius-card",
        "--cel-radius-control",
        "--cel-rail-button-size",
        "--cel-rail-item-size",
        "--cel-rail-width",
        "--cel-search-highlight",
        "--cel-selection-bg",
        "--cel-splitter-width",
        "--cel-text-primary",
        "--cel-text-secondary",
        "--cel-warning-text"
    ];

    [Test]
    public void EveryCssTokenReference_ResolvesToADeclaredToken()
    {
        var source = LoadTokenSource();

        var declaredNames = source.Tokens
            .Where(token => token.EmitsCss)
            .Select(token => token.CssPropertyName!)
            .ToHashSet();

        var references = FindReferences(StyleFileExtensions, CssReferenceRegex);

        references.Should().NotBeEmpty("the source tree styles its web surfaces with the design tokens");

        var failures = references
            .Where(reference => !declaredNames.Contains(reference.Name))
            .Select(reference => $"{reference.Name} referenced in {reference.RelativePath} is not declared in DesignTokens.json")
            .ToList();

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    [Test]
    public void EveryXamlColorReference_ResolvesToADeclaredToken()
    {
        var source = LoadTokenSource();

        var declaredNames = source.Tokens
            .Where(token => token.EmitsXaml)
            .Select(token => token.XamlColorKey!)
            .ToHashSet();

        var references = FindReferences(["*.xaml"], XamlReferenceRegex);

        references.Should().NotBeEmpty("the source tree paints its native chrome with the design tokens");

        var failures = references
            // WinUI ships its own theme colours (SystemAccentColor and the SystemList family). They are
            // not ours to declare, and their names all carry the System prefix.
            .Where(reference => !reference.Name.StartsWith("System", StringComparison.Ordinal))
            .Where(reference => !declaredNames.Contains(reference.Name))
            .Select(reference => $"{reference.Name} referenced in {reference.RelativePath} is not declared in DesignTokens.json")
            .ToList();

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    [Test]
    public void EveryCssToken_HasAConsumer()
    {
        var source = LoadTokenSource();

        var referencedNames = FindReferences(StyleFileExtensions, CssReferenceRegex)
            .Select(reference => reference.Name)
            .ToHashSet();

        var unused = source.Tokens
            .Where(token => token.EmitsCss)
            .Select(token => token.CssPropertyName!)
            // A token declaring a plain CSS property rather than a custom property, such as color-scheme,
            // is read by the engine itself and is never named by a var() reference.
            .Where(name => name.StartsWith("--", StringComparison.Ordinal))
            .Where(name => !referencedNames.Contains(name))
            .Where(name => !TokensWithoutHostConsumer.Contains(name))
            .ToList();

        unused.Should().BeEmpty(
            "a token nothing uses outlives the tone it names while staying in the published contract, so "
            + "give it a consumer, remove it, or list it in TokensWithoutHostConsumer: "
            + string.Join(", ", unused));
    }

    [Test]
    public void EveryXamlToken_HasAConsumer()
    {
        var source = LoadTokenSource();

        var xamlTokens = source.Tokens
            .Where(token => token.EmitsXaml)
            .ToList();

        // A colour exists to feed its brush, so a token counts as used when either name is referenced.
        var candidateNames = xamlTokens
            .SelectMany(token => new[] { token.XamlColorKey, token.XamlBrushKey })
            .OfType<string>()
            .ToList();

        var namesInUse = FindNamesInUse(candidateNames);

        var unused = xamlTokens
            .Where(token => !namesInUse.Contains(token.XamlColorKey!))
            .Where(token => token.XamlBrushKey is null || !namesInUse.Contains(token.XamlBrushKey))
            .Select(token => token.XamlBrushKey ?? token.XamlColorKey!)
            .ToList();

        unused.Should().BeEmpty(
            "a colour or brush nothing paints with is dead weight in the theme dictionaries, so give it a "
            + "consumer or drop the token's XAML target: "
            + string.Join(", ", unused));
    }

    [Test]
    public void ThePublishedTokenSet_MatchesTheContract()
    {
        var source = LoadTokenSource();

        var publishedNames = source.Tokens
            .Where(token => token.Published)
            .Select(token => token.CssPropertyName!)
            .OrderBy(name => name, StringComparer.Ordinal);

        publishedNames.Should().Equal(
            PublishedTokenNames,
            "a published token is part of the contribution contract, so adding, renaming or removing one "
            + "has to be matched here");
    }

    private static DesignTokenSource LoadTokenSource()
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        sourceFolder.Should().NotBeEmpty("the tests locate the repository by walking up to Celbridge.slnx");

        var tokenSourcePath = Path.Combine(sourceFolder, "Core", "Celbridge.DesignTokens", "DesignTokens.json");
        File.Exists(tokenSourcePath).Should().BeTrue($"the token source should be at {tokenSourcePath}");

        return DesignTokenSourceLoader.LoadFromFile(tokenSourcePath);
    }

    private static IReadOnlyList<TokenReference> FindReferences(string[] searchPatterns, Regex referenceRegex)
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        var references = new List<TokenReference>();

        foreach (var filePath in EnumerateSourceFiles(sourceFolder, searchPatterns))
        {
            var relativePath = Path.GetRelativePath(sourceFolder, filePath);
            var content = File.ReadAllText(filePath);

            foreach (Match match in referenceRegex.Matches(content))
            {
                references.Add(new TokenReference(match.Groups[1].Value, relativePath));
            }
        }

        return references;
    }

    // Reports which of the candidate names appear in the native sources, reading only until every name has
    // been accounted for. The generated dictionary declares all of them, and the Tests project names them
    // in its own assertions, so neither counts as a consumer.
    private static IReadOnlySet<string> FindNamesInUse(IReadOnlyList<string> candidateNames)
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        var testsFolder = Path.Combine(sourceFolder, "Tests");
        var namesInUse = new HashSet<string>(StringComparer.Ordinal);
        var remainingNames = new List<string>(candidateNames);

        foreach (var filePath in EnumerateSourceFiles(sourceFolder, NativeFileExtensions))
        {
            if (remainingNames.Count == 0)
            {
                break;
            }

            if (Path.GetFileName(filePath) == "ColorTokens.xaml")
            {
                continue;
            }

            if (filePath.StartsWith(testsFolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = File.ReadAllText(filePath);

            remainingNames.RemoveAll(name =>
            {
                if (!content.Contains(name, StringComparison.Ordinal))
                {
                    return false;
                }

                namesInUse.Add(name);

                return true;
            });
        }

        return namesInUse;
    }

    // Walks the tree a folder at a time so build output and package folders are pruned rather than
    // enumerated. Each carries copies of the web assets, which would be scanned as if they were sources.
    private static IEnumerable<string> EnumerateSourceFiles(string folder, string[] searchPatterns)
    {
        foreach (var searchPattern in searchPatterns)
        {
            foreach (var filePath in Directory.EnumerateFiles(folder, searchPattern))
            {
                yield return filePath;
            }
        }

        foreach (var subFolder in Directory.EnumerateDirectories(folder))
        {
            var folderName = Path.GetFileName(subFolder);
            if (folderName is "bin" or "obj" or "node_modules")
            {
                continue;
            }

            foreach (var filePath in EnumerateSourceFiles(subFolder, searchPatterns))
            {
                yield return filePath;
            }
        }
    }

    private sealed record TokenReference(string Name, string RelativePath);
}

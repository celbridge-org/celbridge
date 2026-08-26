using System.Text.RegularExpressions;

namespace Celbridge.Tests.Architecture;

/// <summary>
/// Guards the XAML namespace prefix convention: a prefix names the same namespace everywhere, so a reader
/// never has to check a file's header to know what it refers to. The exceptions are local and vm, which name
/// the declaring project's own views and view models and so mean something different in each file.
/// </summary>
[TestFixture]
public class XamlNamespacePrefixTests
{
    // Prefixes that are relative by design, naming whichever project the declaring file belongs to.
    private static readonly string[] RelativePrefixes = { "local", "vm" };

    private static readonly Regex NamespaceDeclaration = new(
        @"xmlns:(?<prefix>\w+)=""using:(?<namespace>[\w.]+)""",
        RegexOptions.Compiled);

    // Every XAML file is read once for the fixture rather than once per test.
    private static readonly Lazy<IReadOnlyList<PrefixDeclaration>> Declarations =
        new(ReadDeclarations);

    [Test]
    public void EveryPrefix_NamesOneNamespace()
    {
        var declarations = Declarations.Value;

        var offenders = declarations
            .Where(declaration => !RelativePrefixes.Contains(declaration.Prefix))
            .GroupBy(declaration => declaration.Prefix)
            .Where(group => group.Select(declaration => declaration.Namespace).Distinct().Count() > 1)
            .Select(group => Describe(group.Key, group.Select(declaration => declaration.Namespace)))
            .ToList();

        offenders.Should().BeEmpty(
            "a prefix standing for more than one namespace makes every use of it ambiguous");
    }

    [Test]
    public void EveryNamespace_TakesOnePrefix()
    {
        var declarations = Declarations.Value;

        var offenders = declarations
            .Where(declaration => !RelativePrefixes.Contains(declaration.Prefix))
            .GroupBy(declaration => declaration.Namespace)
            .Where(group => group.Select(declaration => declaration.Prefix).Distinct().Count() > 1)
            .Select(group => Describe(group.Key, group.Select(declaration => declaration.Prefix)))
            .ToList();

        offenders.Should().BeEmpty(
            "a namespace spelled with a different prefix in each file is the drift this convention prevents");
    }

    private static string Describe(string name, IEnumerable<string> values)
    {
        var distinct = values.Distinct().OrderBy(value => value);

        return $"{name} -> {string.Join(", ", distinct)}";
    }

    private static IReadOnlyList<PrefixDeclaration> ReadDeclarations()
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        Directory.Exists(sourceFolder).Should().BeTrue(
            "the repository Source folder must be locatable from the test binary");

        var declarations = new List<PrefixDeclaration>();
        foreach (var filePath in ArchitectureHelpers.EnumerateProductionSourceFiles(sourceFolder, "*.xaml"))
        {
            var contents = ArchitectureHelpers.ReadSourceFile(filePath);
            foreach (Match match in NamespaceDeclaration.Matches(contents))
            {
                var declaration = new PrefixDeclaration(
                    match.Groups["prefix"].Value,
                    match.Groups["namespace"].Value);

                declarations.Add(declaration);
            }
        }

        declarations.Should().NotBeEmpty("the XAML files declaring these prefixes must be readable");

        return declarations;
    }

    private record PrefixDeclaration(string Prefix, string Namespace);
}

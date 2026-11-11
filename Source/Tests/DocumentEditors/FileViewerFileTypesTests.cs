using System.Text.RegularExpressions;
using Celbridge.Packages.Helpers;
using Celbridge.Tests.Architecture;
using Celbridge.Tests.Packages;

namespace Celbridge.Tests.DocumentEditors;

/// <summary>
/// Keeps the File Viewer's file types in step across three lists. The File Viewer's manifest decides which files open
/// in the File Viewer. The page's extension sets decide how the page draws each file. The host's file type catalog
/// decides whether the Open with menu also offers each type to the code editor. A type missing from one list fails
/// these tests.
/// </summary>
[TestFixture]
public class FileViewerFileTypesTests
{
    // An extension set in the page's script, for example `const IMAGE_EXTENSIONS = new Set(['.png', '.jpg']);`.
    private static readonly Regex ExtensionSetPattern = new(@"const\s+[A-Z]+_EXTENSIONS\s*=\s*new\s+Set\(\[(?<items>[^\]]*)\]\)");

    private static readonly Regex QuotedExtensionPattern = new(@"'(?<extension>\.[a-z0-9]+)'");

    [Test]
    public void EveryRegisteredTypeHasARenderer()
    {
        var unrendered = ReadRegisteredExtensions().Except(ReadRenderedExtensions());

        unrendered.Should().BeEmpty("a registered type without a renderer opens to the could-not-be-shown message");
    }

    [Test]
    public void EveryRenderedTypeIsRegistered()
    {
        var unregistered = ReadRenderedExtensions().Except(ReadRegisteredExtensions());

        unregistered.Should().BeEmpty("the File Viewer opens only the types in its manifest, so a renderer for any other type is dead code");
    }

    [Test]
    public void EveryRegisteredTypeIsCataloguedAsBinary()
    {
        var catalog = TestFileTypeCatalog.CreateLoaded();

        var uncatalogued = ReadRegisteredExtensions().Where(extension => !catalog.IsBinaryExtension(extension));

        uncatalogued.Should().BeEmpty("the Open with menu offers the code editor for each type that the catalog doesn't mark as binary");
    }

    private static string FileViewerFolder => Path.Combine(
        ArchitectureHelpers.FindSourceFolder(),
        "Modules",
        "Celbridge.DocumentEditors",
        "Editors",
        "FileViewer");

    private static List<string> ReadRegisteredExtensions()
    {
        var loadResult = PackageManifestLoader.LoadPackage(Path.Combine(FileViewerFolder, "package.toml"));
        loadResult.IsSuccess.Should().BeTrue(loadResult.FirstErrorMessage);

        var editor = loadResult.Value.Editors.Should().ContainSingle().Subject;
        var extensions = editor.FileTypes.Select(fileType => fileType.FileExtension).ToList();

        // An empty list fails the test, so each test always compares at least one type.
        extensions.Should().NotBeEmpty();

        return extensions;
    }

    private static List<string> ReadRenderedExtensions()
    {
        var script = File.ReadAllText(Path.Combine(FileViewerFolder, "js", "file-render.js"));

        var extensions = ExtensionSetPattern.Matches(script)
            .SelectMany(set => QuotedExtensionPattern.Matches(set.Groups["items"].Value))
            .Select(match => match.Groups["extension"].Value)
            .ToList();

        extensions.Should().NotBeEmpty("the page declares the types it draws as extension sets");

        return extensions;
    }
}

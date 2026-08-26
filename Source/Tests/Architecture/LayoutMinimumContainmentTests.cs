using System.Text.RegularExpressions;

namespace Celbridge.Tests.Architecture;

/// <summary>
/// Guards the one-floor rule behind the workspace layout minimums: every minimum is composed from the
/// document floor rather than authored where it is used, so a size floor written as a literal outside the
/// constants files is the scope error the composition was built to remove.
/// </summary>
[TestFixture]
public class LayoutMinimumContainmentTests
{
    // The files a size floor may be authored in. Everything above them is a query rather than a value.
    private static readonly string[] ConstantsFiles =
    {
        Path.Combine("Core", "Celbridge.Foundation", "Workspace", "WorkspaceConstants.cs"),
        Path.Combine("Core", "Celbridge.UserInterface", "Helpers", "WindowMinimumSize.cs")
    };

    // Floors that are not part of the workspace layout, so nothing composes upward from them.
    private static readonly string[] FloorsOutsideTheLayout =
    {
        // The settings panel splitter divides two panes of one document rather than two workspace surfaces.
        // Its floors are local to that document and invisible to the section containing it.
        Path.Combine("Modules", "Celbridge.WebView", "Views", "WebViewDocumentView.xaml.cs"),

        // The viewport a headless WebView is rendered at, which no surface is laid out against.
        Path.Combine("Core", "Celbridge.WebHost", "Platform", "SkiaWebViewAdapter.cs"),

        // How much of a restored window's title bar has to stay on screen, which is a placement check rather
        // than a size floor.
        Path.Combine("Core", "Celbridge.UserInterface", "Helpers", "WindowPlacementPolicy.cs")
    };

    private static readonly Regex AuthoredSizeFloorPattern = new(
        @"(?:const|readonly)\s+(?:double|float|int)\s+\w*Min\w*(?:Width|Height|Size)\w*\s*=\s*-?\d",
        RegexOptions.Compiled);

    private static readonly Regex TrackMinimumInMarkupPattern = new(
        @"<(?:ColumnDefinition|RowDefinition)\b[^>]*\bMin(?:Width|Height)\s*=\s*""\s*-?\d",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex TrackMinimumInCodePattern = new(
        @"\.\s*Min(?:Width|Height)\s*=\s*-?\d",
        RegexOptions.Compiled);

    [Test]
    public void SizeFloors_AreAuthoredOnlyInTheConstantsFiles()
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        Directory.Exists(sourceFolder).Should().BeTrue(
            "the repository Source folder must be locatable from the test binary");

        var offenders = new List<string>();
        foreach (var filePath in ArchitectureHelpers.EnumerateProductionSourceFiles(sourceFolder))
        {
            var relativePath = Path.GetRelativePath(sourceFolder, filePath);
            if (ConstantsFiles.Contains(relativePath) ||
                FloorsOutsideTheLayout.Contains(relativePath))
            {
                continue;
            }

            var contents = ArchitectureHelpers.ReadSourceFile(filePath);
            if (AuthoredSizeFloorPattern.IsMatch(contents))
            {
                offenders.Add(relativePath);
            }
        }

        offenders.Should().BeEmpty(
            "a layout minimum belongs in the design tokens and the constants files, composed upward from there");
    }

    [Test]
    public void GridTrackMinimums_AreComposedRatherThanAuthored()
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        Directory.Exists(sourceFolder).Should().BeTrue(
            "the repository Source folder must be locatable from the test binary");

        var offenders = new List<string>();

        foreach (var filePath in ArchitectureHelpers.EnumerateProductionSourceFiles(sourceFolder, "*.xaml"))
        {
            var contents = ArchitectureHelpers.ReadSourceFile(filePath);
            if (TrackMinimumInMarkupPattern.IsMatch(contents))
            {
                offenders.Add(Path.GetRelativePath(sourceFolder, filePath));
            }
        }

        foreach (var filePath in ArchitectureHelpers.EnumerateProductionSourceFiles(sourceFolder))
        {
            var contents = ArchitectureHelpers.ReadSourceFile(filePath);
            if (TrackMinimumInCodePattern.IsMatch(contents))
            {
                offenders.Add(Path.GetRelativePath(sourceFolder, filePath));
            }
        }

        offenders.Should().BeEmpty(
            "a track floor must be composed from the surfaces it is presenting, never named as a value");
    }
}

using Celbridge.UserInterface.Helpers;

namespace Celbridge.Tests.Helpers;

[TestFixture]
public class PathDisambiguationHelperTests
{
    [Test]
    public void EmptyDictionary_ReturnsEmpty()
    {
        var paths = new Dictionary<string, string>();

        var result = PathDisambiguationHelper.DisambiguatePaths(paths);

        result.Should().BeEmpty();
    }

    [Test]
    public void SinglePath_ReturnsFileNameOnly()
    {
        var paths = new Dictionary<string, string>
        {
            { "key1", @"C:\Projects\MyProject\src\File.txt" }
        };

        var result = PathDisambiguationHelper.DisambiguatePaths(paths);

        result.Should().HaveCount(1);
        result["key1"].Should().Be("File.txt");
    }

    [Test]
    public void TwoPathsWithDifferentParentDirectories_ShowsParentDirectory()
    {
        var paths = new Dictionary<string, string>
        {
            { "key1", @"C:\Projects\ProjectA\File.txt" },
            { "key2", @"C:\Projects\ProjectB\File.txt" }
        };

        var result = PathDisambiguationHelper.DisambiguatePaths(paths);

        result.Should().HaveCount(2);
        result["key1"].Should().Be("ProjectA/File.txt");
        result["key2"].Should().Be("ProjectB/File.txt");
    }

    [Test]
    public void TwoPathsWithSameParentButDifferentGrandparent_ShowsMinimalDifference()
    {
        var paths = new Dictionary<string, string>
        {
            { "key1", @"C:\Projects\ProjectA\src\File.txt" },
            { "key2", @"C:\Projects\ProjectB\src\File.txt" }
        };

        var result = PathDisambiguationHelper.DisambiguatePaths(paths);

        result.Should().HaveCount(2);
        result["key1"].Should().Be("ProjectA/File.txt");
        result["key2"].Should().Be("ProjectB/File.txt");
    }

    [Test]
    public void ThreePathsWithVariousOverlaps_DisambiguatesCorrectly()
    {
        var paths = new Dictionary<string, string>
        {
            { "key1", @"C:\ProjectA\src\components\Button.tsx" },
            { "key2", @"C:\ProjectB\src\components\Button.tsx" },
            { "key3", @"C:\ProjectA\tests\components\Button.tsx" }
        };

        var result = PathDisambiguationHelper.DisambiguatePaths(paths);

        result.Should().HaveCount(3);
        
        // Verify all paths are unique
        result.Values.Should().OnlyHaveUniqueItems();
        
        // Verify they all end with the filename
        result.Values.Should().AllSatisfy(v => v.Should().EndWith("Button.tsx"));
        
        // Verify distinguishing parts are present
        result["key1"].Should().Contain("src");
        result["key2"].Should().Contain("ProjectB");
        result["key3"].Should().Contain("tests");
    }

    [Test]
    public void PathsWithCompletelyDifferentRoots_ShowsMinimalPath()
    {
        var paths = new Dictionary<string, string>
        {
            { "key1", @"C:\Work\File.txt" },
            { "key2", @"D:\Personal\File.txt" }
        };

        var result = PathDisambiguationHelper.DisambiguatePaths(paths);

        result.Should().HaveCount(2);
        result["key1"].Should().Be("Work/File.txt");
        result["key2"].Should().Be("Personal/File.txt");
    }

    [Test]
    public void PathsWithIdenticalDirectories_ShowsFullPath()
    {
        // This is an edge case - same filename in same directory path
        // In practice, this shouldn't happen, but we should handle it gracefully
        var paths = new Dictionary<string, string>
        {
            { "key1", @"C:\Project\src\File.txt" },
            { "key2", @"C:\Project\src\File.txt" }
        };

        var result = PathDisambiguationHelper.DisambiguatePaths(paths);

        result.Should().HaveCount(2);
        // Both should get the same display string since paths are identical
        result["key1"].Should().Be("File.txt");
        result["key2"].Should().Be("File.txt");
    }

    [Test]
    public void DeepNestedPaths_ShowsOnlyNecessarySegments()
    {
        var paths = new Dictionary<string, string>
        {
            { "key1", @"C:\A\B\C\D\E\F\G\File.txt" },
            { "key2", @"C:\A\B\C\D\E\X\G\File.txt" }
        };

        var result = PathDisambiguationHelper.DisambiguatePaths(paths);

        result.Should().HaveCount(2);
        // Only shows the differentiating segments
        result.Values.Should().OnlyHaveUniqueItems();
        result["key1"].Should().Contain("F");
        result["key2"].Should().Contain("X");
    }

    [Test]
    public void PathsWithDifferentDepths_HandlesCorrectly()
    {
        var paths = new Dictionary<string, string>
        {
            { "key1", @"C:\Project\File.txt" },
            { "key2", @"C:\Project\src\components\File.txt" }
        };

        var result = PathDisambiguationHelper.DisambiguatePaths(paths);

        result.Should().HaveCount(2);
        // Shows minimal distinguishing path
        result["key1"].Should().Be("Project/File.txt");
        result["key2"].Should().Be("components/File.txt");
    }

    [Test]
    public void MultiplePathsWithComplexOverlap_DisambiguatesAll()
    {
        var paths = new Dictionary<string, string>
        {
            { "key1", @"C:\Users\Alice\Documents\Project\File.txt" },
            { "key2", @"C:\Users\Bob\Documents\Project\File.txt" },
            { "key3", @"C:\Users\Alice\Downloads\Project\File.txt" },
            { "key4", @"D:\Backup\Alice\Documents\Project\File.txt" }
        };

        var result = PathDisambiguationHelper.DisambiguatePaths(paths);

        result.Should().HaveCount(4);
        
        // Verify all paths are unique
        result.Values.Should().OnlyHaveUniqueItems();
        
        // Verify they all end with the filename
        result.Values.Should().AllSatisfy(v => v.Should().EndWith("File.txt"));
        
        // Verify distinguishing parts are present
        result["key1"].Should().Contain("Documents");
        result["key2"].Should().Contain("Bob");
        result["key3"].Should().Contain("Downloads");
        result["key4"].Should().Contain("Backup");
    }

    [Test]
    public void PathsWithOnlyFileName_HandlesGracefully()
    {
        // Edge case: paths with no directory separators
        var paths = new Dictionary<string, string>
        {
            { "key1", "File.txt" },
            { "key2", "File.txt" }
        };

        var result = PathDisambiguationHelper.DisambiguatePaths(paths);

        result.Should().HaveCount(2);
        result["key1"].Should().Be("File.txt");
        result["key2"].Should().Be("File.txt");
    }

    [Test]
    public void PathsWithMixedFileNameOnlyAndFullPaths_HandlesCorrectly()
    {
        var paths = new Dictionary<string, string>
        {
            { "key1", "File.txt" },
            { "key2", @"C:\Project\File.txt" }
        };

        var result = PathDisambiguationHelper.DisambiguatePaths(paths);

        result.Should().HaveCount(2);
        // Both just show the filename since one has no path to disambiguate with
        result["key1"].Should().Be("File.txt");
        result["key2"].Should().Be("File.txt");
    }

    [Test]
    public void WorksWithIntegerKeys()
    {
        var paths = new Dictionary<int, string>
        {
            { 1, @"C:\ProjectA\File.txt" },
            { 2, @"C:\ProjectB\File.txt" }
        };

        var result = PathDisambiguationHelper.DisambiguatePaths(paths);

        result.Should().HaveCount(2);
        result[1].Should().Be("ProjectA/File.txt");
        result[2].Should().Be("ProjectB/File.txt");
    }

    [Test]
    public void PathsWithEmptySegments_HandlesCorrectly()
    {
        // Edge case: paths with trailing or double separators
        var paths = new Dictionary<string, string>
        {
            { "key1", @"C:\Project\src\\File.txt" }, // Double backslash
            { "key2", @"C:\Project\test\\File.txt" }
        };

        var result = PathDisambiguationHelper.DisambiguatePaths(paths);

        result.Should().HaveCount(2);
        // Should handle empty segments gracefully
        result["key1"].Should().Contain("src");
        result["key2"].Should().Contain("test");
    }

    [Test]
    public void FivePathsWithMixedSimilarities_AllDisambiguated()
    {
        var paths = new Dictionary<string, string>
        {
            { "key1", @"C:\Work\ClientA\Project\src\App.tsx" },
            { "key2", @"C:\Work\ClientB\Project\src\App.tsx" },
            { "key3", @"C:\Personal\MyApp\src\App.tsx" },
            { "key4", @"C:\Work\ClientA\Project\tests\App.tsx" },
            { "key5", @"D:\Archive\Project\src\App.tsx" }
        };

        var result = PathDisambiguationHelper.DisambiguatePaths(paths);

        result.Should().HaveCount(5);
        
        // All should be unique
        var values = result.Values.ToList();
        values.Should().OnlyHaveUniqueItems();
        
        // Verify they all end with the filename
        result.Values.Should().AllSatisfy(v => v.Should().EndWith("App.tsx"));
        
        // Verify distinguishing parts are present - algorithm minimizes path shown
        result["key1"].Should().Contain("src");
        result["key2"].Should().Contain("ClientB");
        result["key3"].Should().Match(v => v.Contains("MyApp") || v.Contains("Personal"));
        result["key4"].Should().Contain("tests");
        result["key5"].Should().Contain("Archive");
    }

    [Test]
    public void PathsWithSpecialCharactersInDirectoryNames_HandlesCorrectly()
    {
        var paths = new Dictionary<string, string>
        {
            { "key1", @"C:\My Projects\Project-1\File.txt" },
            { "key2", @"C:\My Projects\Project-2\File.txt" }
        };

        var result = PathDisambiguationHelper.DisambiguatePaths(paths);

        result.Should().HaveCount(2);
        result["key1"].Should().Be("Project-1/File.txt");
        result["key2"].Should().Be("Project-2/File.txt");
    }
}

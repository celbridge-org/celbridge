using System.IO.Compression;
using Celbridge.Resources.Services;

namespace Celbridge.Tests;

[TestFixture]
public class PackageArchiveTests
{
    private string _testFolderPath = string.Empty;

    [SetUp]
    public void Setup()
    {
        _testFolderPath = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(PackageArchiveTests)}");
        if (Directory.Exists(_testFolderPath))
        {
            Directory.Delete(_testFolderPath, true);
        }
        Directory.CreateDirectory(_testFolderPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testFolderPath))
        {
            Directory.Delete(_testFolderPath, true);
        }
    }

    [Test]
    public void ArchiveFolderCreatesValidZip()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "source");
        Directory.CreateDirectory(sourceFolder);
        File.WriteAllText(Path.Combine(sourceFolder, "readme.md"), "Hello");
        File.WriteAllText(Path.Combine(sourceFolder, "data.txt"), "World");

        var subFolder = Path.Combine(sourceFolder, "sub");
        Directory.CreateDirectory(subFolder);
        File.WriteAllText(Path.Combine(subFolder, "nested.txt"), "Nested content");

        var archivePath = Path.Combine(_testFolderPath, "output.zip");

        CreateArchiveFromFolder(sourceFolder, archivePath);

        File.Exists(archivePath).Should().BeTrue();

        using var zipArchive = ZipFile.OpenRead(archivePath);
        zipArchive.Entries.Count.Should().Be(3);

        var entryNames = zipArchive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
        entryNames.Should().Contain("data.txt");
        entryNames.Should().Contain("readme.md");
        entryNames.Should().Contain("sub/nested.txt");
    }

    [Test]
    public void ArchiveSingleFileCreatesValidZip()
    {
        var filePath = Path.Combine(_testFolderPath, "single.txt");
        File.WriteAllText(filePath, "Single file content");

        var archivePath = Path.Combine(_testFolderPath, "single.zip");

        using (var fileStream = new FileStream(archivePath, FileMode.Create))
        using (var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            var entry = zipArchive.CreateEntry("single.txt", CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var sourceStream = File.OpenRead(filePath);
            sourceStream.CopyTo(entryStream);
        }

        using var readArchive = ZipFile.OpenRead(archivePath);
        readArchive.Entries.Count.Should().Be(1);
        readArchive.Entries[0].FullName.Should().Be("single.txt");
    }

    [Test]
    public void UnarchiveExtractsFilesCorrectly()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "source");
        Directory.CreateDirectory(sourceFolder);

        var originalContent = "Hello, archive world!";
        File.WriteAllText(Path.Combine(sourceFolder, "file.txt"), originalContent);

        var subFolder = Path.Combine(sourceFolder, "sub");
        Directory.CreateDirectory(subFolder);
        File.WriteAllText(Path.Combine(subFolder, "nested.txt"), "Nested");

        var archivePath = Path.Combine(_testFolderPath, "test.zip");
        CreateArchiveFromFolder(sourceFolder, archivePath);

        var destinationFolder = Path.Combine(_testFolderPath, "destination");

        ExtractArchive(archivePath, destinationFolder);

        var extractedContent = File.ReadAllText(Path.Combine(destinationFolder, "file.txt"));
        extractedContent.Should().Be(originalContent);

        var extractedNested = File.ReadAllText(Path.Combine(destinationFolder, "sub", "nested.txt"));
        extractedNested.Should().Be("Nested");
    }

    [Test]
    public void RoundTripPreservesContent()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "source");
        Directory.CreateDirectory(sourceFolder);
        File.WriteAllText(Path.Combine(sourceFolder, "a.txt"), "Content A");
        File.WriteAllText(Path.Combine(sourceFolder, "b.txt"), "Content B");

        var subFolder = Path.Combine(sourceFolder, "deep/nested");
        Directory.CreateDirectory(subFolder);
        File.WriteAllText(Path.Combine(subFolder, "c.txt"), "Content C");

        var archivePath = Path.Combine(_testFolderPath, "roundtrip.zip");
        CreateArchiveFromFolder(sourceFolder, archivePath);

        var destinationFolder = Path.Combine(_testFolderPath, "destination");
        ExtractArchive(archivePath, destinationFolder);

        File.ReadAllText(Path.Combine(destinationFolder, "a.txt")).Should().Be("Content A");
        File.ReadAllText(Path.Combine(destinationFolder, "b.txt")).Should().Be("Content B");
        File.ReadAllText(Path.Combine(destinationFolder, "deep", "nested", "c.txt")).Should().Be("Content C");
    }

    [Test]
    public void ZipSlipEntriesAreRejected()
    {
        var archivePath = Path.Combine(_testFolderPath, "malicious.zip");

        using (var fileStream = new FileStream(archivePath, FileMode.Create))
        using (var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            var entry = zipArchive.CreateEntry("../escape.txt");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write("malicious content");
        }

        var isValid = ValidateArchiveEntries(archivePath);
        isValid.Should().BeFalse("entries with '..' should be rejected");
    }

    [Test]
    public void BackslashEntriesAreRejected()
    {
        var archivePath = Path.Combine(_testFolderPath, "backslash.zip");

        using (var fileStream = new FileStream(archivePath, FileMode.Create))
        using (var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            var entry = zipArchive.CreateEntry("folder\\file.txt");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write("content");
        }

        var isValid = ValidateArchiveEntries(archivePath);
        isValid.Should().BeFalse("entries with backslashes should be rejected");
    }

    [Test]
    public void OverwriteFalseBlocksExistingFiles()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "source");
        Directory.CreateDirectory(sourceFolder);
        File.WriteAllText(Path.Combine(sourceFolder, "file.txt"), "Original");

        var archivePath = Path.Combine(_testFolderPath, "test.zip");
        CreateArchiveFromFolder(sourceFolder, archivePath);

        var destinationFolder = Path.Combine(_testFolderPath, "destination");
        Directory.CreateDirectory(destinationFolder);
        File.WriteAllText(Path.Combine(destinationFolder, "file.txt"), "Existing");

        var action = () => ExtractArchive(archivePath, destinationFolder, overwrite: false);
        action.Should().Throw<InvalidOperationException>();

        var content = File.ReadAllText(Path.Combine(destinationFolder, "file.txt"));
        content.Should().Be("Existing");
    }

    [Test]
    public void OverwriteTrueReplacesExistingFiles()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "source");
        Directory.CreateDirectory(sourceFolder);
        File.WriteAllText(Path.Combine(sourceFolder, "file.txt"), "New content");

        var archivePath = Path.Combine(_testFolderPath, "test.zip");
        CreateArchiveFromFolder(sourceFolder, archivePath);

        var destinationFolder = Path.Combine(_testFolderPath, "destination");
        Directory.CreateDirectory(destinationFolder);
        File.WriteAllText(Path.Combine(destinationFolder, "file.txt"), "Old content");

        ExtractArchive(archivePath, destinationFolder, overwrite: true);

        var content = File.ReadAllText(Path.Combine(destinationFolder, "file.txt"));
        content.Should().Be("New content");
    }

    [Test]
    public void IncludeFilterSelectsMatchingFiles()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "source");
        Directory.CreateDirectory(sourceFolder);
        File.WriteAllText(Path.Combine(sourceFolder, "code.py"), "print('hello')");
        File.WriteAllText(Path.Combine(sourceFolder, "readme.md"), "# Readme");
        File.WriteAllText(Path.Combine(sourceFolder, "data.csv"), "a,b,c");

        var archivePath = Path.Combine(_testFolderPath, "filtered.zip");
        CreateArchiveFromFolder(sourceFolder, archivePath, include: "*.py;*.md");

        using var zipArchive = ZipFile.OpenRead(archivePath);
        zipArchive.Entries.Count.Should().Be(2);

        var entryNames = zipArchive.Entries.Select(e => e.FullName).ToList();
        entryNames.Should().Contain("code.py");
        entryNames.Should().Contain("readme.md");
        entryNames.Should().NotContain("data.csv");
    }

    [Test]
    public void ExcludeFilterRemovesMatchingFiles()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "source");
        Directory.CreateDirectory(sourceFolder);
        File.WriteAllText(Path.Combine(sourceFolder, "code.py"), "print('hello')");
        File.WriteAllText(Path.Combine(sourceFolder, "readme.md"), "# Readme");

        var cacheFolder = Path.Combine(sourceFolder, "__pycache__");
        Directory.CreateDirectory(cacheFolder);
        File.WriteAllText(Path.Combine(cacheFolder, "cached.pyc"), "bytecode");

        var archivePath = Path.Combine(_testFolderPath, "filtered.zip");
        CreateArchiveFromFolder(sourceFolder, archivePath, exclude: "__pycache__");

        using var zipArchive = ZipFile.OpenRead(archivePath);
        var entryNames = zipArchive.Entries.Select(e => e.FullName).ToList();
        entryNames.Should().Contain("code.py");
        entryNames.Should().Contain("readme.md");
        entryNames.Should().NotContain("__pycache__/cached.pyc");
    }

    [Test]
    public void EmptyFolderProducesEmptyZip()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "empty");
        Directory.CreateDirectory(sourceFolder);

        var archivePath = Path.Combine(_testFolderPath, "empty.zip");
        CreateArchiveFromFolder(sourceFolder, archivePath);

        File.Exists(archivePath).Should().BeTrue();

        using var zipArchive = ZipFile.OpenRead(archivePath);
        zipArchive.Entries.Count.Should().Be(0);
    }

    [Test]
    public void UnarchiveCreatesDestinationFolderIfMissing()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "source");
        Directory.CreateDirectory(sourceFolder);
        File.WriteAllText(Path.Combine(sourceFolder, "file.txt"), "Content");

        var archivePath = Path.Combine(_testFolderPath, "test.zip");
        CreateArchiveFromFolder(sourceFolder, archivePath);

        var destinationFolder = Path.Combine(_testFolderPath, "new", "nested", "destination");
        Directory.Exists(destinationFolder).Should().BeFalse();

        ExtractArchive(archivePath, destinationFolder);

        File.Exists(Path.Combine(destinationFolder, "file.txt")).Should().BeTrue();
    }

    [Test]
    public void ZipBombProtectionAbortsLargeExtraction()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "source");
        Directory.CreateDirectory(sourceFolder);
        File.WriteAllText(Path.Combine(sourceFolder, "file.txt"), new string('A', 1000));

        var archivePath = Path.Combine(_testFolderPath, "bomb.zip");
        CreateArchiveFromFolder(sourceFolder, archivePath);

        var destinationFolder = Path.Combine(_testFolderPath, "destination");

        // Extract with a tiny size limit to trigger the bomb check
        var action = () => ExtractArchive(archivePath, destinationFolder, maxExtractedBytes: 100);
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*exceeds*maximum*");
    }

    [Test]
    public void ArchiveDoesNotOverwriteByDefault()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "source");
        Directory.CreateDirectory(sourceFolder);
        File.WriteAllText(Path.Combine(sourceFolder, "file.txt"), "Content");

        var archivePath = Path.Combine(_testFolderPath, "existing.zip");
        File.WriteAllText(archivePath, "placeholder");

        var action = () => CreateArchiveFromFolder(sourceFolder, archivePath, overwrite: false);
        action.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void AbsolutePathEntriesAreRejected()
    {
        var archivePath = Path.Combine(_testFolderPath, "absolute.zip");

        using (var fileStream = new FileStream(archivePath, FileMode.Create))
        using (var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            var entry = zipArchive.CreateEntry("/etc/passwd");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write("root:x:0:0");
        }

        var isValid = ValidateArchiveEntries(archivePath);
        isValid.Should().BeFalse("entries with absolute paths should be rejected");
    }

    [Test]
    public void DirectoryEntriesAreSkippedDuringExtraction()
    {
        var archivePath = Path.Combine(_testFolderPath, "with_dirs.zip");

        using (var fileStream = new FileStream(archivePath, FileMode.Create))
        using (var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            // Add a directory entry (trailing slash)
            var dirEntry = zipArchive.CreateEntry("subfolder/");

            // Add a real file entry
            var fileEntry = zipArchive.CreateEntry("subfolder/data.txt");
            using var entryStream = fileEntry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write("data");
        }

        var destinationFolder = Path.Combine(_testFolderPath, "destination");
        ExtractArchive(archivePath, destinationFolder);

        File.Exists(Path.Combine(destinationFolder, "subfolder", "data.txt")).Should().BeTrue();
        File.ReadAllText(Path.Combine(destinationFolder, "subfolder", "data.txt")).Should().Be("data");
    }

    [Test]
    public void IncludeAndExcludeCombinedFilterCorrectly()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "source");
        Directory.CreateDirectory(sourceFolder);
        File.WriteAllText(Path.Combine(sourceFolder, "app.py"), "main");
        File.WriteAllText(Path.Combine(sourceFolder, "test_app.py"), "test");
        File.WriteAllText(Path.Combine(sourceFolder, "readme.md"), "docs");
        File.WriteAllText(Path.Combine(sourceFolder, "notes.txt"), "notes");

        var archivePath = Path.Combine(_testFolderPath, "combined.zip");
        CreateArchiveFromFolder(sourceFolder, archivePath, include: "*.py;*.md", exclude: "test_*");

        using var zipArchive = ZipFile.OpenRead(archivePath);
        var entryNames = zipArchive.Entries.Select(e => e.FullName).ToList();
        entryNames.Should().Contain("app.py");
        entryNames.Should().Contain("readme.md");
        entryNames.Should().NotContain("test_app.py");
        entryNames.Should().NotContain("notes.txt");
    }

    [Test]
    public void MultipleExcludePatternsAllApply()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "source");
        Directory.CreateDirectory(sourceFolder);
        File.WriteAllText(Path.Combine(sourceFolder, "code.py"), "code");
        File.WriteAllText(Path.Combine(sourceFolder, "debug.log"), "log data");
        File.WriteAllText(Path.Combine(sourceFolder, "backup.bak"), "backup");

        var archivePath = Path.Combine(_testFolderPath, "multi_exclude.zip");
        CreateArchiveFromFolder(sourceFolder, archivePath, exclude: "*.log;*.bak");

        using var zipArchive = ZipFile.OpenRead(archivePath);
        var entryNames = zipArchive.Entries.Select(e => e.FullName).ToList();
        entryNames.Should().Contain("code.py");
        entryNames.Should().NotContain("debug.log");
        entryNames.Should().NotContain("backup.bak");
    }

    [Test]
    public void BinaryContentPreservedThroughRoundTrip()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "source");
        Directory.CreateDirectory(sourceFolder);

        var binaryContent = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            binaryContent[i] = (byte)i;
        }
        File.WriteAllBytes(Path.Combine(sourceFolder, "data.bin"), binaryContent);

        var archivePath = Path.Combine(_testFolderPath, "binary.zip");
        CreateArchiveFromFolder(sourceFolder, archivePath);

        var destinationFolder = Path.Combine(_testFolderPath, "destination");
        ExtractArchive(archivePath, destinationFolder);

        var extractedBytes = File.ReadAllBytes(Path.Combine(destinationFolder, "data.bin"));
        extractedBytes.Should().Equal(binaryContent);
    }

    [Test]
    public void UnicodeFilenamesPreservedThroughRoundTrip()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "source");
        Directory.CreateDirectory(sourceFolder);
        File.WriteAllText(Path.Combine(sourceFolder, "notes.txt"), "content");

        var archivePath = Path.Combine(_testFolderPath, "unicode.zip");
        CreateArchiveFromFolder(sourceFolder, archivePath);

        var destinationFolder = Path.Combine(_testFolderPath, "destination");
        ExtractArchive(archivePath, destinationFolder);

        File.ReadAllText(Path.Combine(destinationFolder, "notes.txt")).Should().Be("content");
    }

    [Test]
    public void InvalidZipFileIsRejected()
    {
        var archivePath = Path.Combine(_testFolderPath, "not_a_zip.zip");
        File.WriteAllText(archivePath, "this is not a zip file");

        var destinationFolder = Path.Combine(_testFolderPath, "destination");

        var action = () => ExtractArchive(archivePath, destinationFolder);
        action.Should().Throw<InvalidDataException>();
    }

    [Test]
    public void EmptySegmentEntriesAreRejected()
    {
        var archivePath = Path.Combine(_testFolderPath, "empty_segment.zip");

        using (var fileStream = new FileStream(archivePath, FileMode.Create))
        using (var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            var entry = zipArchive.CreateEntry("folder//file.txt");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write("content");
        }

        var isValid = ValidateArchiveEntries(archivePath);
        isValid.Should().BeFalse("entries with empty segments should be rejected");
    }

    [Test]
    public void DeeplyNestedFolderStructurePreserved()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "source");
        var deepFolder = Path.Combine(sourceFolder, "a", "b", "c", "d", "e");
        Directory.CreateDirectory(deepFolder);
        File.WriteAllText(Path.Combine(deepFolder, "deep.txt"), "deep content");

        var archivePath = Path.Combine(_testFolderPath, "deep.zip");
        CreateArchiveFromFolder(sourceFolder, archivePath);

        var destinationFolder = Path.Combine(_testFolderPath, "destination");
        ExtractArchive(archivePath, destinationFolder);

        var extractedPath = Path.Combine(destinationFolder, "a", "b", "c", "d", "e", "deep.txt");
        File.Exists(extractedPath).Should().BeTrue();
        File.ReadAllText(extractedPath).Should().Be("deep content");
    }

    [Test]
    public void ExcludePatternMatchesFolderNamesInPath()
    {
        var sourceFolder = Path.Combine(_testFolderPath, "source");
        Directory.CreateDirectory(sourceFolder);
        File.WriteAllText(Path.Combine(sourceFolder, "keep.txt"), "keep");

        var nodeModules = Path.Combine(sourceFolder, "node_modules", "pkg");
        Directory.CreateDirectory(nodeModules);
        File.WriteAllText(Path.Combine(nodeModules, "index.js"), "module");

        var gitFolder = Path.Combine(sourceFolder, ".git", "objects");
        Directory.CreateDirectory(gitFolder);
        File.WriteAllText(Path.Combine(gitFolder, "abc123"), "object");

        var archivePath = Path.Combine(_testFolderPath, "exclude_folders.zip");
        CreateArchiveFromFolder(sourceFolder, archivePath, exclude: "node_modules;.git");

        using var zipArchive = ZipFile.OpenRead(archivePath);
        var entryNames = zipArchive.Entries.Select(e => e.FullName).ToList();
        entryNames.Should().Contain("keep.txt");
        entryNames.Should().NotContain("node_modules/pkg/index.js");
        entryNames.Should().NotContain(".git/objects/abc123");
    }

    [Test]
    public void ShouldIncludeFileReturnsTrueWhenNoFilters()
    {
        var includeRegexes = ArchiveHelper.ParseGlobPatterns("");
        var excludeRegexes = ArchiveHelper.ParseGlobPatterns("");

        ArchiveHelper.ShouldIncludeFile("any_file.txt", includeRegexes, excludeRegexes).Should().BeTrue();
    }

    [Test]
    public void ShouldIncludeFileRespectsExcludeOnly()
    {
        var includeRegexes = ArchiveHelper.ParseGlobPatterns("");
        var excludeRegexes = ArchiveHelper.ParseGlobPatterns("*.log");

        ArchiveHelper.ShouldIncludeFile("app.py", includeRegexes, excludeRegexes).Should().BeTrue();
        ArchiveHelper.ShouldIncludeFile("debug.log", includeRegexes, excludeRegexes).Should().BeFalse();
    }

    [Test]
    public void ShouldIncludeFileRespectsIncludeOnly()
    {
        var includeRegexes = ArchiveHelper.ParseGlobPatterns("*.py");
        var excludeRegexes = ArchiveHelper.ParseGlobPatterns("");

        ArchiveHelper.ShouldIncludeFile("app.py", includeRegexes, excludeRegexes).Should().BeTrue();
        ArchiveHelper.ShouldIncludeFile("readme.md", includeRegexes, excludeRegexes).Should().BeFalse();
    }

    [Test]
    public void ShouldIncludeFileExcludeTakesPrecedenceOverInclude()
    {
        var includeRegexes = ArchiveHelper.ParseGlobPatterns("*.py");
        var excludeRegexes = ArchiveHelper.ParseGlobPatterns("test_*");

        ArchiveHelper.ShouldIncludeFile("app.py", includeRegexes, excludeRegexes).Should().BeTrue();
        ArchiveHelper.ShouldIncludeFile("test_app.py", includeRegexes, excludeRegexes).Should().BeFalse();
    }

    [Test]
    public void ShouldIncludeFileExcludeMatchesPathSegments()
    {
        var includeRegexes = ArchiveHelper.ParseGlobPatterns("");
        var excludeRegexes = ArchiveHelper.ParseGlobPatterns("__pycache__");

        ArchiveHelper.ShouldIncludeFile("__pycache__/module.pyc", includeRegexes, excludeRegexes).Should().BeFalse();
        ArchiveHelper.ShouldIncludeFile("src/__pycache__/module.pyc", includeRegexes, excludeRegexes).Should().BeFalse();
        ArchiveHelper.ShouldIncludeFile("src/module.py", includeRegexes, excludeRegexes).Should().BeTrue();
    }

    [Test]
    public void ParseGlobPatternsHandlesEmptyInput()
    {
        ArchiveHelper.ParseGlobPatterns("").Should().BeEmpty();
        ArchiveHelper.ParseGlobPatterns("  ").Should().BeEmpty();
    }

    [Test]
    public void ParseGlobPatternsHandlesMultiplePatterns()
    {
        var regexes = ArchiveHelper.ParseGlobPatterns("*.py;*.md;*.txt");
        regexes.Count.Should().Be(3);

        regexes[0].IsMatch("app.py").Should().BeTrue();
        regexes[1].IsMatch("readme.md").Should().BeTrue();
        regexes[2].IsMatch("notes.txt").Should().BeTrue();
        regexes[0].IsMatch("readme.md").Should().BeFalse();
    }

    [Test]
    public void ParseGlobPatternsTrimsWhitespace()
    {
        var regexes = ArchiveHelper.ParseGlobPatterns(" *.py ; *.md ");
        regexes.Count.Should().Be(2);

        regexes[0].IsMatch("app.py").Should().BeTrue();
        regexes[1].IsMatch("readme.md").Should().BeTrue();
    }

    [Test]
    public void GlobToRegexMatchesWildcards()
    {
        var regex = new System.Text.RegularExpressions.Regex(ArchiveHelper.GlobToRegex("*.py"));
        regex.IsMatch("app.py").Should().BeTrue();
        regex.IsMatch("app.txt").Should().BeFalse();
    }

    [Test]
    public void GlobToRegexMatchesQuestionMark()
    {
        var regex = new System.Text.RegularExpressions.Regex(ArchiveHelper.GlobToRegex("file?.txt"));
        regex.IsMatch("file1.txt").Should().BeTrue();
        regex.IsMatch("fileA.txt").Should().BeTrue();
        regex.IsMatch("file12.txt").Should().BeFalse();
    }

    [Test]
    public void CollectFolderHierarchyCollectsAllLevels()
    {
        var destinationPath = Path.Combine(_testFolderPath, "dest");
        var deepFolder = Path.Combine(destinationPath, "a", "b", "c");

        var foldersToCreate = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        ArchiveHelper.CollectFolderHierarchy(deepFolder, destinationPath, foldersToCreate);

        foldersToCreate.Count.Should().Be(3);
        foldersToCreate.Should().Contain(Path.GetFullPath(Path.Combine(destinationPath, "a")));
        foldersToCreate.Should().Contain(Path.GetFullPath(Path.Combine(destinationPath, "a", "b")));
        foldersToCreate.Should().Contain(Path.GetFullPath(Path.Combine(destinationPath, "a", "b", "c")));
    }

    [Test]
    public void CollectFolderHierarchyStopsAtDestination()
    {
        var destinationPath = Path.Combine(_testFolderPath, "dest");
        var childFolder = Path.Combine(destinationPath, "only_one");

        var foldersToCreate = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        ArchiveHelper.CollectFolderHierarchy(childFolder, destinationPath, foldersToCreate);

        foldersToCreate.Count.Should().Be(1);
        foldersToCreate.Should().Contain(Path.GetFullPath(childFolder));
    }

    private static void CreateArchiveFromFolder(
        string sourceFolderPath,
        string archivePath,
        string include = "",
        string exclude = "",
        bool overwrite = true)
    {
        if (!overwrite && File.Exists(archivePath))
        {
            throw new InvalidOperationException($"Archive already exists: '{archivePath}'");
        }

        var includeRegexes = ArchiveHelper.ParseGlobPatterns(include);
        var excludeRegexes = ArchiveHelper.ParseGlobPatterns(exclude);

        using var fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);

        var filePaths = Directory.GetFiles(sourceFolderPath, "*", SearchOption.AllDirectories);

        foreach (var filePath in filePaths)
        {
            var fileAttributes = File.GetAttributes(filePath);
            if (fileAttributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(sourceFolderPath, filePath);
            var entryName = relativePath.Replace('\\', '/');

            if (!ArchiveHelper.ShouldIncludeFile(entryName, includeRegexes, excludeRegexes))
            {
                continue;
            }

            var entry = zipArchive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var sourceStream = File.OpenRead(filePath);
            sourceStream.CopyTo(entryStream);
        }
    }

    private static void ExtractArchive(
        string archivePath,
        string destinationFolderPath,
        bool overwrite = true,
        long maxExtractedBytes = long.MaxValue)
    {
        long totalExtractedBytes = 0;

        using var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read);

        foreach (var entry in zipArchive.Entries)
        {
            var entryName = entry.FullName;

            if (string.IsNullOrEmpty(entryName) || entryName.EndsWith('/'))
            {
                continue;
            }

            if (!ResourceKey.IsValidKey(entryName))
            {
                throw new InvalidOperationException(
                    $"Archive contains an invalid entry name: '{entryName}'");
            }

            totalExtractedBytes += entry.Length;
            if (totalExtractedBytes > maxExtractedBytes)
            {
                throw new InvalidOperationException(
                    $"Archive exceeds the maximum extracted size. Aborting extraction.");
            }

            var outputPath = Path.Combine(destinationFolderPath, entryName.Replace('/', Path.DirectorySeparatorChar));

            var normalizedOutputPath = Path.GetFullPath(outputPath);
            var normalizedDestinationPath = Path.GetFullPath(destinationFolderPath + Path.DirectorySeparatorChar);
            if (!normalizedOutputPath.StartsWith(normalizedDestinationPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Archive entry '{entryName}' would extract outside the destination folder.");
            }

            if (!overwrite && File.Exists(outputPath))
            {
                throw new InvalidOperationException(
                    $"File already exists: '{outputPath}'");
            }

            var outputFolder = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputFolder) && !Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            using var entryStream = entry.Open();
            using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            entryStream.CopyTo(outputStream);
        }
    }

    private static bool ValidateArchiveEntries(string archivePath)
    {
        using var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read);

        foreach (var entry in zipArchive.Entries)
        {
            var entryName = entry.FullName;

            if (string.IsNullOrEmpty(entryName) || entryName.EndsWith('/'))
            {
                continue;
            }

            if (!ResourceKey.IsValidKey(entryName))
            {
                return false;
            }
        }

        return true;
    }
}
